using SmartStylist.Modules.Catalog.Collection;

namespace SmartStylist.Modules.Catalog.Tests;

public class PriceParserTests
{
    [Theory]
    [InlineData("59.40", 59.40)]
    [InlineData("59.4", 59.40)]
    [InlineData("$1,299.00", 1299.00)]
    [InlineData("1.299,00 €", 1299.00)]
    [InlineData("59,40", 59.40)]
    [InlineData("1,299", 1299)]
    [InlineData("₾169.50", 169.50)]
    [InlineData("12.345", 12.35)]
    public void Parses_shop_price_formats(string raw, decimal expected) =>
        Assert.Equal(expected, PriceParser.Parse(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("free")]
    [InlineData("0.00")]
    public void Rejects_missing_or_zero_prices(string? raw) => Assert.Null(PriceParser.Parse(raw));
}
