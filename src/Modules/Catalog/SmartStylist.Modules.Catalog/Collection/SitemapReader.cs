using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace SmartStylist.Modules.Catalog.Collection;

internal sealed record SitemapResult(IReadOnlyList<string> Urls, string? LastError);

/// <summary>Finds product page URLs through robots.txt and (nested, possibly gzipped) sitemaps.</summary>
internal sealed class SitemapReader(ILogger<SitemapReader> logger)
{
    private const int MaxSitemapFetches = 25;

    public async Task<SitemapResult> GetProductUrlsAsync(
        IPageFetcher fetcher,
        Uri shopRoot,
        string? sitemapUrl,
        Regex? productUrlPattern,
        int wanted,
        CancellationToken cancellationToken)
    {
        string? lastError = null;
        var queue = new Queue<Uri>(await StartingSitemapsAsync(fetcher, shopRoot, sitemapUrl, error => lastError = error, cancellationToken));
        var visited = new HashSet<Uri>();
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0 && visited.Count < MaxSitemapFetches && urls.Count < wanted)
        {
            var sitemap = queue.Dequeue();
            if (!visited.Add(sitemap))
            {
                continue;
            }

            XDocument xml;
            try
            {
                var bytes = await fetcher.GetFileAsync(sitemap, cancellationToken);
                xml = XDocument.Parse(Decode(bytes));
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or InvalidDataException or TimeoutException or Microsoft.Playwright.PlaywrightException)
            {
                logger.LogWarning("Could not read sitemap {Sitemap}: {Error}", sitemap, FirstLine(ex.Message));
                lastError = $"sitemap {sitemap}: {FirstLine(ex.Message)}";
                continue;
            }

            var root = xml.Root;
            if (root is null)
            {
                continue;
            }

            var locs = root.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value.Trim()).ToList();
            if (root.Name.LocalName == "sitemapindex")
            {
                foreach (var child in PreferProductSitemaps(locs))
                {
                    if (Uri.TryCreate(child, UriKind.Absolute, out var childUri))
                    {
                        queue.Enqueue(childUri);
                    }
                }

                continue;
            }

            foreach (var loc in locs)
            {
                if ((productUrlPattern is null || productUrlPattern.IsMatch(loc)) && seen.Add(loc))
                {
                    urls.Add(loc);
                    if (urls.Count >= wanted)
                    {
                        break;
                    }
                }
            }
        }

        return new SitemapResult(urls, lastError);
    }

    internal static IEnumerable<string> PreferProductSitemaps(IReadOnlyList<string> locs)
    {
        var productSitemaps = locs.Where(l => l.Contains("product", StringComparison.OrdinalIgnoreCase)).ToList();
        return productSitemaps.Count > 0 ? productSitemaps : locs;
    }

    internal static IReadOnlyList<Uri> SitemapsFromRobots(string robotsTxt, Uri shopRoot) =>
        robotsTxt.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("sitemap:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["sitemap:".Length..].Trim())
            .Select(value => Uri.TryCreate(shopRoot, value, out var uri) ? uri : null)
            .OfType<Uri>()
            .Distinct()
            .ToList();

    private async Task<IReadOnlyList<Uri>> StartingSitemapsAsync(
        IPageFetcher fetcher, Uri shopRoot, string? sitemapUrl, Action<string> onError, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sitemapUrl))
        {
            return [new Uri(shopRoot, sitemapUrl)];
        }

        try
        {
            var robots = Encoding.UTF8.GetString(await fetcher.GetFileAsync(new Uri(shopRoot, "/robots.txt"), cancellationToken));
            var fromRobots = SitemapsFromRobots(robots, shopRoot);
            if (fromRobots.Count > 0)
            {
                return fromRobots;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Microsoft.Playwright.PlaywrightException)
        {
            logger.LogWarning("Could not read robots.txt for {Shop}: {Error}", shopRoot, FirstLine(ex.Message));
            onError($"robots.txt: {FirstLine(ex.Message)}");
        }

        return [new Uri(shopRoot, "/sitemap.xml")];
    }

    internal static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();

    private static string Decode(byte[] bytes)
    {
        if (bytes is [0x1f, 0x8b, ..])
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
