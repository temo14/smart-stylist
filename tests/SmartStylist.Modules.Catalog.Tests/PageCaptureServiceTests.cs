using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Tests;

public class PageCaptureServiceTests
{
    private const string PageUrl = "https://www.levi.com/US/en_US/jeans/p/045415";

    private static readonly ShopOptions Levis = new() { Key = "levis", Name = "Levi's", BaseUrl = "https://www.levi.com/US/en_US", Currency = "USD", Enabled = false };

    private static string Page(decimal price) => $$"""
        <link rel="canonical" href="{{PageUrl}}">
        <script type="application/ld+json">
        {"@type":"Product","sku":"045415","name":"541 Jeans","offers":{"@type":"Offer","price":"{{price}}","priceCurrency":"USD","availability":"https://schema.org/InStock"} }
        </script>
        """;

    private static Task<PageCaptureResult> CaptureAsync(TestHost host, string url, string html) =>
        host.InScopeAsync(sp => sp.GetRequiredService<PageCaptureService>().CaptureAsync(new PageCapture(url, html), CancellationToken.None));

    private static Task<List<PriceSnapshot>> SnapshotsAsync(TestHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().PriceSnapshots.OrderBy(s => s.ObservedOn).ToListAsync());

    [Fact]
    public async Task Stores_the_price_of_a_blocked_shop_page()
    {
        await using var host = new TestHost(Levis);
        await host.InitializeAsync();

        var result = await CaptureAsync(host, PageUrl, Page(59.40m));

        Assert.Equal(PageCaptureStatus.Saved, result.Status);
        Assert.Equal("Levi's", result.Shop);
        Assert.Equal(59.40m, result.Price);
        Assert.Equal(1, result.DaysTracked);

        var snapshot = Assert.Single(await SnapshotsAsync(host));
        Assert.Equal(PriceSource.Extension, snapshot.Source);
        Assert.Equal(59.40m, snapshot.Price);
    }

    [Fact]
    public async Task A_second_visit_on_the_same_day_replaces_the_price()
    {
        await using var host = new TestHost(Levis);
        await host.InitializeAsync();

        await CaptureAsync(host, PageUrl, Page(99m));
        var result = await CaptureAsync(host, PageUrl, Page(59.40m));

        Assert.Equal(1, result.DaysTracked);
        Assert.Equal(59.40m, Assert.Single(await SnapshotsAsync(host)).Price);
    }

    [Fact]
    public async Task Reports_the_lowest_price_across_days()
    {
        await using var host = new TestHost(Levis);
        await host.InitializeAsync();

        await CaptureAsync(host, PageUrl, Page(59.40m));
        host.Time.Advance(TimeSpan.FromDays(1));
        var result = await CaptureAsync(host, PageUrl, Page(99m));

        Assert.Equal(99m, result.Price);
        Assert.Equal(59.40m, result.LowestPrice);
        Assert.Equal(2, result.DaysTracked);
    }

    [Fact]
    public async Task Joins_a_product_first_seen_in_a_sale_email()
    {
        await using var host = new TestHost(Levis);
        await host.InitializeAsync();
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            var shop = await db.Shops.SingleAsync();
            db.Products.Add(new Product { ShopId = shop.Id, ExternalId = PageUrl, Url = PageUrl, Title = "Jeans", FirstSeenAt = host.Time.GetUtcNow() });
            await db.SaveChangesAsync();
            return 0;
        });

        await CaptureAsync(host, PageUrl, Page(59.40m));

        var products = await host.InScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().Products.ToListAsync());
        Assert.Equal("541 Jeans", Assert.Single(products).Title);
    }

    [Theory]
    [InlineData("https://www.example.com/p/1")]
    [InlineData("not a url")]
    [InlineData("ftp://www.levi.com/p/1")]
    public async Task Ignores_pages_outside_the_shop_list(string url)
    {
        await using var host = new TestHost(Levis);
        await host.InitializeAsync();

        var result = await CaptureAsync(host, url, Page(59.40m));

        Assert.Equal(PageCaptureStatus.UnknownShop, result.Status);
        Assert.Empty(await SnapshotsAsync(host));
    }

    [Fact]
    public async Task Says_so_when_a_page_has_no_price_data()
    {
        await using var host = new TestHost(Levis);
        await host.InitializeAsync();

        var result = await CaptureAsync(host, PageUrl, "<p>Our story</p>");

        Assert.Equal(PageCaptureStatus.NoPrice, result.Status);
        Assert.Empty(await SnapshotsAsync(host));
    }
}
