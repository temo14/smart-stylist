using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Persistence;
using SmartStylist.Modules.Catalog.Reporting;

namespace SmartStylist.Modules.Catalog.Tests;

public class PriceCollectionServiceTests
{
    private const string Root = "https://shop.example.com";

    private static ShopOptions Shop(string? pattern = null) => new()
    {
        Key = "example",
        Name = "Example",
        BaseUrl = Root + "/en",
        Currency = "USD",
        ProductUrlPattern = pattern,
    };

    private static string Page(string sku, decimal price, decimal? listPrice = null) => $$"""
        <html><head><script type="application/ld+json">
        { "@type": "Product", "name": "Item {{sku}}", "sku": "{{sku}}",
          "offers": { "@type": "Offer", "sku": "{{sku}}-M", "price": "{{price}}", "priceCurrency": "USD",
                      "availability": "https://schema.org/InStock",
                      "itemOffered": { "size": "M" }
                      {{(listPrice is null ? "" : $", \"priceSpecification\": {{ \"priceType\": \"https://schema.org/ListPrice\", \"price\": {listPrice} }}")}} } }
        </script></head></html>
        """;

    private static void ServeSitemapShop(FakeWeb web, int products, Func<int, decimal> price)
    {
        web.Serve($"{Root}/robots.txt", $"User-agent: *\nSitemap: {Root}/sitemap_index.xml", "text/plain");
        web.Serve($"{Root}/sitemap_index.xml", $"""
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>{Root}/sitemap-pages.xml</loc></sitemap>
              <sitemap><loc>{Root}/sitemap-products.xml</loc></sitemap>
            </sitemapindex>
            """, "application/xml");
        var locs = string.Concat(Enumerable.Range(1, products).Select(i => $"<url><loc>{Root}/p/item-{i}</loc></url>"));
        web.Serve($"{Root}/sitemap-products.xml",
            $"""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>{Root}/about</loc></url>{locs}</urlset>""",
            "application/xml");
        for (var i = 1; i <= products; i++)
        {
            web.Serve($"{Root}/p/item-{i}", Page($"SKU{i}", price(i), i == 1 ? 100m : null));
        }
    }

    private static Task<CollectionRun> CollectAsync(TestHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<PriceCollectionService>().CollectAsync("example", null, CancellationToken.None));

    [Fact]
    public async Task Auto_falls_back_from_shopify_to_sitemap_json_ld_and_remembers_it()
    {
        await using var host = new TestHost(Shop(pattern: "/p/"));
        await host.InitializeAsync();
        ServeSitemapShop(host.Web, products: 3, price: i => 40m + i);

        var run = await CollectAsync(host);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(CollectorStrategy.SitemapJsonLd, run.Strategy);
        Assert.Equal(3, run.ProductsSeen);
        Assert.Equal(3, run.SnapshotsWritten);
        Assert.Contains($"{Root}/products.json?limit=250&page=1", host.Web.Requested);
        Assert.DoesNotContain($"{Root}/sitemap-pages.xml", host.Web.Requested);
        Assert.DoesNotContain($"{Root}/about", host.Web.Requested);

        var (shop, snapshot) = await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            return (await db.Shops.SingleAsync(),
                await db.PriceSnapshots.Include(s => s.Product).SingleAsync(s => s.Product.ExternalId == "SKU1"));
        });
        Assert.Equal(CollectorStrategy.SitemapJsonLd, shop.DetectedStrategy);
        Assert.Equal(41m, snapshot.Price);
        Assert.Equal(100m, snapshot.ListPrice);
        Assert.Equal("M", snapshot.SizeLabel);
        Assert.Equal(new DateOnly(2026, 10, 24), snapshot.ObservedOn);
    }

    [Fact]
    public async Task Keeps_one_price_per_variant_per_day_and_builds_history_across_days()
    {
        await using var host = new TestHost(Shop(pattern: "/p/"));
        await host.InitializeAsync();
        ServeSitemapShop(host.Web, products: 2, price: _ => 50m);

        await CollectAsync(host);
        await CollectAsync(host); // same day again

        host.Time.Advance(TimeSpan.FromDays(1));
        host.Web.Serve($"{Root}/p/item-2", Page("SKU2", 35m, 50m));
        var secondDay = await CollectAsync(host);

        Assert.Equal(CollectorStrategy.SitemapJsonLd, secondDay.Strategy);
        // Day two tries the remembered strategy first, so no Shopify probe.
        Assert.Single(host.Web.Requested, $"{Root}/products.json?limit=250&page=1".Equals);

        var (snapshots, report) = await host.InScopeAsync(async sp =>
            (await sp.GetRequiredService<CatalogDbContext>().PriceSnapshots.CountAsync(),
             await sp.GetRequiredService<DataQualityReport>().BuildAsync(CancellationToken.None)));

        Assert.Equal(4, snapshots);
        var row = Assert.Single(report);
        Assert.Equal(2, row.DaysOfHistory);
        Assert.Equal(1, row.ProductsWithPriceChange);
        Assert.Equal(2, row.ProductsTracked);
        Assert.Equal(1.0, row.PageSuccessRate);
        Assert.Equal(1.0, row.SizeCoverage);
        Assert.Equal(1.0, row.OnSaleShare); // item 1 always shows a list price, item 2 dropped to 35 from 50
        Assert.Equal(DataGrade.Clean, row.Grade);
    }

    [Fact]
    public async Task Uses_shopify_feed_when_the_shop_has_one()
    {
        await using var host = new TestHost(Shop());
        await host.InitializeAsync();
        host.Web.Serve($"{Root}/products.json?limit=250&page=1", FixtureFile.Read("shopify-products.json"), "application/json");
        host.Web.Serve($"{Root}/products.json?limit=250&page=2", """{"products":[]}""", "application/json");

        var run = await CollectAsync(host);

        Assert.Equal(CollectorStrategy.Shopify, run.Strategy);
        Assert.Equal(2, run.ProductsSeen);
        Assert.Equal(3, run.SnapshotsWritten);
    }

    [Fact]
    public async Task Gives_up_early_when_pages_have_no_price_and_reports_failure()
    {
        await using var host = new TestHost(Shop(pattern: "/p/"));
        await host.InitializeAsync();
        ServeSitemapShop(host.Web, products: 20, price: _ => 10m);
        for (var i = 1; i <= 20; i++)
        {
            host.Web.Serve($"{Root}/p/item-{i}", FixtureFile.Read("no-price.html"));
        }

        var run = await CollectAsync(host);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("first 5 product pages gave no price", run.Error);
        Assert.Contains("Shopify: no feed", run.Error);
        Assert.Equal(5, host.Web.Requested.Count(u => u.Contains("/p/item-")));

        var report = await host.InScopeAsync(sp => sp.GetRequiredService<DataQualityReport>().BuildAsync(CancellationToken.None));
        Assert.Equal(DataGrade.Failing, Assert.Single(report).Grade);
    }

    [Fact]
    public async Task Disables_shops_removed_from_config()
    {
        await using var host = new TestHost(Shop());
        await host.InitializeAsync();
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            db.Shops.Add(new Domain.Shop { Key = "old", Name = "Old", BaseUrl = "https://old.example.com", Currency = "USD" });
            await db.SaveChangesAsync();
            return await sp.GetRequiredService<ShopCatalogSync>().SyncAsync(CancellationToken.None);
        });

        var shops = await host.InScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().Shops.OrderBy(s => s.Key).ToListAsync());

        Assert.Collection(shops, s => Assert.True(s.Enabled), s => Assert.False(s.Enabled));
    }
}
