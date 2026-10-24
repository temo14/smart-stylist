namespace SmartStylist.Modules.Catalog.Collection;

public sealed record ProbeResult(string? Html, ScrapedProduct? Product, string? Error);

/// <summary>Fetches one product page and shows what the parser reads from it. Used to tune a new or failing shop.</summary>
public sealed class ProductPageProbe
{
    private readonly HttpPageFetcher _http;
    private readonly BrowserPageFetcher _browser;

    internal ProductPageProbe(HttpPageFetcher http, BrowserPageFetcher browser)
    {
        _http = http;
        _browser = browser;
    }

    public async Task<ProbeResult> ProbeAsync(Uri url, bool useBrowser, string fallbackCurrency, CancellationToken cancellationToken)
    {
        string html;
        try
        {
            html = useBrowser
                ? await _browser.GetPageAsync(url, cancellationToken)
                : await _http.GetPageAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CollectorUnusableException or Microsoft.Playwright.PlaywrightException)
        {
            return new ProbeResult(null, null, SitemapReader.FirstLine(ex.Message));
        }

        var product = JsonLdProductParser.Parse(html, url, fallbackCurrency);
        return new ProbeResult(html, product, product is null ? "no JSON-LD, microdata or price meta tags found" : null);
    }
}
