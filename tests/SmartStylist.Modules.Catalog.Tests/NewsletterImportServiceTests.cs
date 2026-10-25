using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Newsletters;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Tests;

public class NewsletterImportServiceTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 23, 14, 5, 0, TimeSpan.Zero);

    private static readonly ShopOptions JCrew = new() { Key = "jcrew", Name = "J.Crew", BaseUrl = "https://www.jcrew.com", Currency = "USD", Enabled = false };

    private static InboxMessage Sale(string id = "<1@e.jcrew.com>", string from = "jcrew@e.jcrew.com") => new(
        id, from, "Extra 40% off", Received,
        """
        <a href="https://click.e.jcrew.com/chino">Slim-fit chino</a> <s>$89.50</s> $53.70
        <a href="https://click.e.jcrew.com/oxford">Oxford shirt</a> $59.40
        <a href="https://click.e.jcrew.com/tee">Tee</a> 40% off
        """,
        null);

    private static NewsletterExtraction SaleExtraction() => new(
        HasProductPrices: true,
        SaleEndsOn: "2026-10-25",
        PromoCode: "SHOPNOW",
        Products:
        [
            new ExtractedProduct("Slim-fit chino", "L1", "$53.70", "$89.50", "USD"),
            new ExtractedProduct("Oxford shirt", "L2", "$59.40", null, "USD"),
            new ExtractedProduct("Tee", "L3", "$19.99", null, "USD"), // made up: not in the email
        ]);

    private static Task<NewsletterImportResult> ImportAsync(TestHost host, params InboxMessage[] messages) =>
        host.InScopeAsync(sp => sp.GetRequiredService<NewsletterImportService>().ImportAsync(new ListMailSource(messages), CancellationToken.None));

    private static Task<List<PriceSnapshot>> SnapshotsAsync(TestHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().PriceSnapshots.Include(s => s.Product).OrderBy(s => s.Price).ToListAsync());

    [Fact]
    public async Task Prices_from_a_sale_email_are_checked_and_stored_on_the_email_date()
    {
        await using var host = new TestHost(JCrew);
        await host.InitializeAsync();
        host.Extractor.Respond = _ => SaleExtraction();
        host.Links.Targets["https://click.e.jcrew.com/chino"] = new Uri("https://www.jcrew.com/p/mens/chino/BE123");

        var result = await ImportAsync(host, Sale());

        var email = Assert.Single(result.Processed);
        Assert.Equal(NewsletterStatus.Priced, email.Status);
        Assert.Equal(3, email.ProductsFound);
        Assert.Equal(1, email.ProductsRejected);
        Assert.Equal(2, email.PricesWritten);
        Assert.Equal(new DateOnly(2026, 10, 25), email.SaleEndsOn);
        Assert.Equal("SHOPNOW", email.PromoCode);
        Assert.Equal("J.Crew", host.Extractor.Inputs.Single().ShopName);

        var snapshots = await SnapshotsAsync(host);
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, s =>
        {
            Assert.Equal(PriceSource.Newsletter, s.Source);
            Assert.Equal(new DateOnly(2026, 10, 23), s.ObservedOn);
        });

        var chino = snapshots[0];
        Assert.Equal(53.70m, chino.Price);
        Assert.Equal(89.50m, chino.ListPrice);
        Assert.Equal("https://www.jcrew.com/p/mens/chino/BE123", chino.Product.ExternalId);

        // The oxford link did not resolve, so the product is keyed by title and keeps the tracking link.
        var oxford = snapshots[1];
        Assert.Equal("email:oxford shirt", oxford.Product.ExternalId);
        Assert.Equal("https://click.e.jcrew.com/oxford", oxford.Product.Url);
    }

    [Fact]
    public async Task The_same_email_is_read_once()
    {
        await using var host = new TestHost(JCrew);
        await host.InitializeAsync();
        host.Extractor.Respond = _ => SaleExtraction();

        await ImportAsync(host, Sale());
        var second = await ImportAsync(host, Sale());

        Assert.Empty(second.Processed);
        Assert.Equal(1, second.AlreadyRead);
        Assert.Single(host.Extractor.Inputs);
    }

    [Fact]
    public async Task Failed_emails_are_retried_on_the_next_read()
    {
        await using var host = new TestHost(JCrew);
        await host.InitializeAsync();
        host.Extractor.Respond = _ => throw new InvalidOperationException("API overloaded");

        var first = await ImportAsync(host, Sale());
        Assert.Equal(NewsletterStatus.Failed, first.Processed.Single().Status);
        Assert.Equal("API overloaded", first.Processed.Single().Error);

        host.Extractor.Respond = _ => SaleExtraction();
        var second = await ImportAsync(host, Sale());

        Assert.Equal(NewsletterStatus.Priced, second.Processed.Single().Status);
        Assert.Null(second.Processed.Single().Error);
        var count = await host.InScopeAsync(sp => sp.GetRequiredService<CatalogDbContext>().NewsletterEmails.CountAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Emails_from_unknown_senders_are_not_sent_to_the_model()
    {
        await using var host = new TestHost(JCrew);
        await host.InitializeAsync();

        var result = await ImportAsync(host, Sale(from: "deals@phishy-jcrew.com"));

        Assert.Equal(NewsletterStatus.UnknownSender, result.Processed.Single().Status);
        Assert.Empty(host.Extractor.Inputs);
    }

    [Fact]
    public async Task A_crawled_price_for_the_same_day_is_not_overwritten()
    {
        await using var host = new TestHost(JCrew);
        await host.InitializeAsync();
        var page = "https://www.jcrew.com/p/mens/chino/BE123";
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            var shop = await db.Shops.SingleAsync();
            var product = new Product { ShopId = shop.Id, ExternalId = "BE123", Url = page, Title = "Chino" };
            db.PriceSnapshots.Add(new PriceSnapshot { Product = product, VariantKey = "", ObservedOn = new DateOnly(2026, 10, 23), Price = 60m, Currency = "USD" });
            return await db.SaveChangesAsync();
        });
        host.Extractor.Respond = _ => SaleExtraction();
        host.Links.Targets["https://click.e.jcrew.com/chino"] = new Uri(page);

        var result = await ImportAsync(host, Sale());

        Assert.Equal(1, result.Processed.Single().PricesWritten); // only the oxford shirt
        var chino = (await SnapshotsAsync(host)).Single(s => s.Product.ExternalId == "BE123");
        Assert.Equal(60m, chino.Price);
        Assert.Equal(PriceSource.Crawl, chino.Source);
    }

    [Fact]
    public async Task An_email_without_product_prices_is_marked_so()
    {
        await using var host = new TestHost(JCrew);
        await host.InitializeAsync();

        var result = await ImportAsync(host, Sale());

        Assert.Equal(NewsletterStatus.NoPrices, result.Processed.Single().Status);
        Assert.Empty(await SnapshotsAsync(host));
    }
}
