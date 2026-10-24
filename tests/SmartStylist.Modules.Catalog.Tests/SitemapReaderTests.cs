using SmartStylist.Modules.Catalog.Collection;

namespace SmartStylist.Modules.Catalog.Tests;

public class SitemapReaderTests
{
    [Fact]
    public void Reads_sitemap_lines_from_robots_txt()
    {
        const string robots = """
            User-agent: *
            Disallow: /cart
            Sitemap: https://shop.example.com/sitemap_index.xml
            sitemap: /sitemap-products.xml
            """;

        var sitemaps = SitemapReader.SitemapsFromRobots(robots, new Uri("https://shop.example.com"));

        Assert.Equal(
            ["https://shop.example.com/sitemap_index.xml", "https://shop.example.com/sitemap-products.xml"],
            sitemaps.Select(u => u.ToString()));
    }

    [Fact]
    public void Prefers_product_sitemaps_in_an_index()
    {
        string[] locs = ["https://s.com/sitemap-pages.xml", "https://s.com/sitemap-products-1.xml", "https://s.com/sitemap-blog.xml"];

        Assert.Equal(["https://s.com/sitemap-products-1.xml"], SitemapReader.PreferProductSitemaps(locs));
    }

    [Fact]
    public void Uses_every_sitemap_when_none_is_named_product()
    {
        string[] locs = ["https://s.com/a.xml", "https://s.com/b.xml"];

        Assert.Equal(locs, SitemapReader.PreferProductSitemaps(locs));
    }

    [Fact]
    public void Ranks_the_shops_own_locale_first_and_drops_media_sitemaps()
    {
        string[] fromRobots =
        [
            "https://www.levi.com/FI/en/sitemap.xml",
            "https://www.levi.com/sitemap_kr_image.xml",
            "https://www.levi.com/US/en_US/sitemap.xml",
        ];

        var ranked = SitemapReader.Rank(fromRobots, SitemapReader.Tokens("/US/en_US")).ToList();

        Assert.Equal(["https://www.levi.com/US/en_US/sitemap.xml", "https://www.levi.com/FI/en/sitemap.xml"], ranked);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><head><meta charset=utf-8></head></html>", "web page")]
    [InlineData("   ", "empty")]
    public void Explains_responses_that_are_not_sitemaps(string body, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => SitemapReader.Decode(System.Text.Encoding.UTF8.GetBytes(body)));

        Assert.Contains(expected, ex.Message);
    }
}
