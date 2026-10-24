using SmartStylist.Modules.Catalog.Collection;

namespace SmartStylist.Modules.Catalog.Tests;

public class JsonLdProductParserTests
{
    private static readonly Uri PageUrl = new("https://shop.example.com/p/oxford-shirt?color=white");

    [Fact]
    public void Reads_product_with_offer_per_size_and_strikethrough_price()
    {
        var product = JsonLdProductParser.Parse(FixtureFile.Read("jsonld-product.html"), PageUrl, "USD");

        Assert.NotNull(product);
        Assert.Equal("Oxford Shirt & Tie", product.Title);
        Assert.Equal("OX-1", product.ExternalId);
        Assert.Equal("https://shop.example.com/p/oxford-shirt", product.Url);
        Assert.Equal("Tommy Hilfiger", product.Brand);
        Assert.Equal("https://cdn.example.com/ox1.jpg", product.ImageUrl);
        Assert.Equal(2, product.Offers.Count);

        var medium = product.Offers[0];
        Assert.Equal("OX-1-M", medium.VariantKey);
        Assert.Equal("M", medium.SizeLabel);
        Assert.Equal(169.50m, medium.Price);
        Assert.Equal(339m, medium.ListPrice);
        Assert.Equal("GEL", medium.Currency);
        Assert.True(medium.InStock);

        var large = product.Offers[1];
        Assert.Equal("L", large.SizeLabel);
        Assert.Null(large.ListPrice);
        Assert.False(large.InStock);
    }

    [Fact]
    public void Reads_product_group_variants_inside_graph()
    {
        var product = JsonLdProductParser.Parse(FixtureFile.Read("jsonld-productgroup.html"), PageUrl, "USD");

        Assert.NotNull(product);
        Assert.Equal("18181-0000", product.ExternalId);
        Assert.Equal("Levi's", product.Brand);
        Assert.Equal("https://cdn.example.com/541.jpg", product.ImageUrl);
        Assert.Collection(product.Offers,
            o => { Assert.Equal("W36 L34", o.SizeLabel); Assert.Equal(59.40m, o.Price); Assert.True(o.InStock); },
            o => { Assert.Equal("181810001-3432", o.VariantKey); Assert.Equal(1099m, o.Price); Assert.False(o.InStock); });
    }

    [Fact]
    public void Uses_low_price_of_aggregate_offer()
    {
        var product = JsonLdProductParser.Parse(FixtureFile.Read("jsonld-aggregate.html"), PageUrl, "GEL");

        var offer = Assert.Single(product!.Offers);
        Assert.Equal(129m, offer.Price);
        Assert.Equal("USD", offer.Currency);
        Assert.Equal("TJ-9", product.ExternalId);
    }

    [Fact]
    public void Falls_back_to_price_meta_tags_when_json_ld_is_broken()
    {
        var product = JsonLdProductParser.Parse(FixtureFile.Read("meta-only.html"), PageUrl, "USD");

        Assert.NotNull(product);
        Assert.Equal("Wool Sweater", product.Title);
        var offer = Assert.Single(product.Offers);
        Assert.Equal(89.90m, offer.Price);
        Assert.Equal("EUR", offer.Currency);
    }

    [Fact]
    public void Returns_null_when_page_has_no_price() =>
        Assert.Null(JsonLdProductParser.Parse(FixtureFile.Read("no-price.html"), PageUrl, "USD"));

    [Fact]
    public void Reads_schema_org_microdata()
    {
        var product = JsonLdProductParser.Parse(FixtureFile.Read("microdata.html"), PageUrl, "USD");

        Assert.NotNull(product);
        Assert.Equal("Linen Shirt", product.Title);
        Assert.Equal("LS-77", product.ExternalId);
        Assert.Equal("Massimo", product.Brand);
        var offer = Assert.Single(product.Offers);
        Assert.Equal(149m, offer.Price);
        Assert.Equal("GEL", offer.Currency);
        Assert.True(offer.InStock);
    }
}
