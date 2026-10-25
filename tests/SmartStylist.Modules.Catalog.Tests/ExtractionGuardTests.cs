using SmartStylist.Modules.Catalog.Newsletters;

namespace SmartStylist.Modules.Catalog.Tests;

public class ExtractionGuardTests
{
    private static NewsletterText Email(string body) =>
        NewsletterText.From(new InboxMessage("id", "a@b.com", "s", DateTimeOffset.UnixEpoch, $"<p>{body}</p>", null), 10_000);

    private static NewsletterExtraction With(params ExtractedProduct[] products) => new(true, null, null, products);

    [Fact]
    public void Keeps_prices_printed_in_the_email()
    {
        var email = Email("""<a href="https://x.com/chino">Chino</a> <s>$89.50</s> $53.70""");

        var (kept, rejected) = ExtractionGuard.Check(With(new ExtractedProduct("Chino", "L1", "$53.70", "$89.50", "USD")), email, "USD");

        var offer = Assert.Single(kept);
        Assert.Equal(0, rejected);
        Assert.Equal(53.70m, offer.Price);
        Assert.Equal(89.50m, offer.ListPrice);
        Assert.Equal("https://x.com/chino", offer.LinkUrl);
    }

    [Theory]
    [InlineData("$49.00")] // not in the email at all
    [InlineData("$59")] // only appears inside $159.00
    [InlineData("free")]
    public void Rejects_prices_not_printed_in_the_email(string price)
    {
        var email = Email("Jacket $159.00 today");

        var (kept, rejected) = ExtractionGuard.Check(With(new ExtractedProduct("Jacket", null, price, null, "USD")), email, "USD");

        Assert.Empty(kept);
        Assert.Equal(1, rejected);
    }

    [Fact]
    public void Drops_a_list_price_that_is_not_higher_or_not_printed()
    {
        var email = Email("Tee $20.00 Hoodie $40.00");

        var (kept, _) = ExtractionGuard.Check(
            With(new ExtractedProduct("Tee", null, "$20.00", "$15.00", null), new ExtractedProduct("Hoodie", null, "$40.00", "$80.00", null)),
            email, "USD");

        Assert.All(kept, o => Assert.Null(o.ListPrice));
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void Currency_comes_from_the_symbol_then_the_model_then_the_shop()
    {
        var email = Email("A 59,40 € B 120 C 35.00");

        var (kept, _) = ExtractionGuard.Check(
            With(
                new ExtractedProduct("A", null, "59,40 €", null, "USD"),
                new ExtractedProduct("B", null, "120", null, "gel"),
                new ExtractedProduct("C", null, "35.00", null, "dollars")),
            email, "USD");

        Assert.Equal(["EUR", "GEL", "USD"], kept.Select(o => o.Currency));
        Assert.Equal(59.40m, kept[0].Price);
    }

    [Theory]
    [InlineData("2026-10-25", 2026, 10, 25)]
    [InlineData("Sunday", 0, 0, 0)]
    [InlineData(null, 0, 0, 0)]
    public void Sale_end_accepts_only_iso_dates(string? value, int year, int month, int day)
    {
        var expected = year == 0 ? (DateOnly?)null : new DateOnly(year, month, day);
        Assert.Equal(expected, ExtractionGuard.SaleEnd(value));
    }
}
