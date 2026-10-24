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
}
