namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>The domains a shop sends email from and hosts its pages on.</summary>
public sealed class ShopDomains
{
    private readonly string[] _domains;
    private readonly string _site;

    public ShopDomains(string shopKey, string baseUrl, IEnumerable<string> emailDomains)
    {
        ShopKey = shopKey;
        var site = new Uri(baseUrl).Host.ToLowerInvariant();
        if (site.StartsWith("www.", StringComparison.Ordinal))
        {
            site = site[4..];
        }

        _site = site;
        _domains = emailDomains
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Append(site)
            .Distinct()
            .ToArray();
    }

    public string ShopKey { get; }

    /// <summary>True for a sender at the domain itself or any subdomain, e.g. news@email.reebok.com for reebok.com.</summary>
    public bool MatchesSender(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 0)
        {
            return false;
        }

        var host = address[(at + 1)..].Trim().ToLowerInvariant();
        return _domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>
    /// True only for the shop's own site host, with or without www. Subdomains such as
    /// click.e.jcrew.com are tracking servers, not product pages.
    /// </summary>
    public bool IsShopPage(string host)
    {
        host = host.ToLowerInvariant();
        return host == _site || host == "www." + _site;
    }
}
