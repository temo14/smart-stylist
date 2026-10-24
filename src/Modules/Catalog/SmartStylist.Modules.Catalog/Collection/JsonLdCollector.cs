using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStylist.Modules.Catalog.Domain;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>
/// Visits product pages found in the sitemap and reads their JSON-LD price. Products priced on earlier
/// days are visited first, so the same items build up a daily history.
/// </summary>
internal abstract class JsonLdCollector(
    IPageFetcher fetcher,
    SitemapReader sitemapReader,
    IOptions<CatalogOptions> options,
    ILogger logger) : IShopCollector
{
    /// <summary>Give up on the strategy when this many pages in a row at the start yield nothing.</summary>
    private const int ProbePages = 5;

    public abstract CollectorStrategy Strategy { get; }

    public virtual async Task<CollectorResult> CollectAsync(ShopRequest request, CancellationToken cancellationToken)
    {
        var pattern = string.IsNullOrWhiteSpace(request.ProductUrlPattern)
            ? null
            : new Regex(request.ProductUrlPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

        var urls = request.KnownProductUrls.Take(request.MaxProducts).ToList();
        string? sitemapError = null;
        if (urls.Count < request.MaxProducts)
        {
            var fromSitemap = await sitemapReader.GetProductUrlsAsync(
                fetcher, request.ShopRoot, request.BaseUrl, request.SitemapUrl, pattern, request.MaxProducts * 2, cancellationToken);
            var known = new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
            urls.AddRange(fromSitemap.Urls.Where(known.Add).Take(request.MaxProducts - urls.Count));
            sitemapError = fromSitemap.LastError;
        }

        if (urls.Count == 0)
        {
            throw new CollectorUnusableException(
                $"{Strategy}: no product URLs in the sitemap ({sitemapError ?? "check ProductUrlPattern or set SitemapUrl"})");
        }

        var products = new List<ScrapedProduct>();
        var failed = 0;
        string? lastError = null;
        foreach (var url in urls)
        {
            if (products.Count == 0 && failed >= ProbePages)
            {
                throw new CollectorUnusableException(
                    $"{Strategy}: first {ProbePages} product pages gave no price. Last error: {lastError ?? "no JSON-LD or price meta"}");
            }

            try
            {
                var html = await fetcher.GetPageAsync(new Uri(url), cancellationToken);
                var product = JsonLdProductParser.Parse(html, new Uri(url), request.Currency);
                if (product is { Offers.Count: > 0 })
                {
                    products.Add(product);
                }
                else
                {
                    failed++;
                    lastError = "no JSON-LD or price meta";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or Microsoft.Playwright.PlaywrightException
                                       && !cancellationToken.IsCancellationRequested)
            {
                failed++;
                lastError = SitemapReader.FirstLine(ex.Message);
                logger.LogDebug("Failed {Url}: {Error}", url, lastError);
            }

            await Task.Delay(options.Value.RequestDelayMs, cancellationToken);
        }

        if (products.Count == 0)
        {
            throw new CollectorUnusableException(
                $"{Strategy}: none of {urls.Count} product pages gave a price. Last error: {lastError ?? "no JSON-LD or price meta"}");
        }

        return new CollectorResult(products, failed);
    }
}

internal sealed class SitemapJsonLdCollector(
    HttpPageFetcher fetcher,
    SitemapReader sitemapReader,
    IOptions<CatalogOptions> options,
    ILogger<SitemapJsonLdCollector> logger) : JsonLdCollector(fetcher, sitemapReader, options, logger)
{
    public override CollectorStrategy Strategy => CollectorStrategy.SitemapJsonLd;
}

internal sealed class BrowserJsonLdCollector(
    BrowserPageFetcher fetcher,
    SitemapReader sitemapReader,
    IOptions<CatalogOptions> options,
    ILogger<BrowserJsonLdCollector> logger) : JsonLdCollector(fetcher, sitemapReader, options, logger)
{
    public override CollectorStrategy Strategy => CollectorStrategy.BrowserJsonLd;

    public override async Task<CollectorResult> CollectAsync(ShopRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.CollectAsync(request, cancellationToken);
        }
        finally
        {
            await fetcher.ResetAsync();
        }
    }
}
