using SmartStylist.Modules.Catalog.Collection;

namespace SmartStylist.Modules.Catalog.Tests;

public class ShopifyFeedParserTests
{
    private static readonly Uri Root = new("https://shop.example.com");

    [Fact]
    public void Maps_products_variants_sizes_and_compare_at_price()
    {
        var products = ShopifyFeedParser.Parse(FixtureFile.Read("shopify-products.json"), Root, "USD");

        Assert.Equal(2, products.Count);
        var chino = products[0];
        Assert.Equal("7001", chino.ExternalId);
        Assert.Equal("https://shop.example.com/products/slim-chino", chino.Url);
        Assert.Equal("Example Co", chino.Brand);
        Assert.Equal("https://cdn.example.com/chino.jpg", chino.ImageUrl);

        var first = chino.Offers[0];
        Assert.Equal("9001", first.VariantKey);
        Assert.Equal("32 / 32", first.SizeLabel);
        Assert.Equal(59.40m, first.Price);
        Assert.Equal(99.00m, first.ListPrice);
        Assert.Equal("USD", first.Currency);
        Assert.True(first.InStock);

        // compare_at equal to price is not a discount.
        Assert.Null(chino.Offers[1].ListPrice);
        Assert.False(chino.Offers[1].InStock);
    }

    [Fact]
    public void Default_title_variant_has_no_size()
    {
        var giftCard = ShopifyFeedParser.Parse(FixtureFile.Read("shopify-products.json"), Root, "USD")[1];

        Assert.Null(Assert.Single(giftCard.Offers).SizeLabel);
    }

    [Fact]
    public void Rejects_json_that_is_not_a_feed() =>
        Assert.Throws<FormatException>(() => ShopifyFeedParser.Parse("{\"error\":\"nope\"}", Root, "USD"));
}
