using SmartStylist.Modules.Catalog.Newsletters;

namespace SmartStylist.Modules.Catalog.Tests;

public class ShopDomainsTests
{
    [Theory]
    [InlineData("jcrew@e.jcrew.com", true)]
    [InlineData("news@jcrew.com", true)]
    [InlineData("deals@notjcrew.com", false)]
    [InlineData("jcrew@e.jcrew.com.evil.io", false)]
    [InlineData("hello@levi.com", true)] // listed in EmailDomains
    public void Senders_match_the_shop_domain_and_its_subdomains(string address, bool expected)
    {
        var shop = new ShopDomains("jcrew", "https://www.jcrew.com/us", ["@levi.com"]);

        Assert.Equal(expected, shop.MatchesSender(address));
    }

    [Theory]
    [InlineData("www.jcrew.com", true)]
    [InlineData("jcrew.com", true)]
    [InlineData("click.e.jcrew.com", false)]
    public void Only_the_site_host_counts_as_a_product_page(string host, bool expected) =>
        Assert.Equal(expected, new ShopDomains("jcrew", "https://www.jcrew.com", []).IsShopPage(host));
}
