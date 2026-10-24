using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace SmartStylist.Modules.Catalog.Collection;

internal sealed record SitemapResult(IReadOnlyList<string> Urls, string? LastError);

/// <summary>Finds product page URLs through robots.txt and (nested, possibly gzipped) sitemaps.</summary>
internal sealed partial class SitemapReader(ILogger<SitemapReader> logger)
{
    private const int MaxSitemapFetches = 25;

    public async Task<SitemapResult> GetProductUrlsAsync(
        IPageFetcher fetcher,
        Uri shopRoot,
        Uri baseUrl,
        string? sitemapUrl,
        Regex? productUrlPattern,
        int wanted,
        CancellationToken cancellationToken)
    {
        string? lastError = null;
        var localeHints = Tokens(baseUrl.AbsolutePath);
        var starting = await StartingSitemapsAsync(fetcher, shopRoot, sitemapUrl, error => lastError = error, cancellationToken);
        var queue = new Queue<Uri>(Rank(starting.Select(u => u.ToString()), localeHints).Select(u => new Uri(u)));
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
                foreach (var child in Rank(PreferProductSitemaps(locs), localeHints))
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

    /// <summary>
    /// Orders sitemaps so the shop's own locale comes first (robots.txt often lists every country)
    /// and drops image and video sitemaps, which hold no product pages.
    /// </summary>
    internal static IEnumerable<string> Rank(IEnumerable<string> sitemaps, IReadOnlySet<string> localeHints)
    {
        var list = sitemaps.ToList();
        var pages = list.Where(s => !MediaSitemap().IsMatch(s)).ToList();
        return (pages.Count > 0 ? pages : list)
            .OrderByDescending(s => Tokens(Uri.TryCreate(s, UriKind.Absolute, out var uri) ? uri.AbsolutePath : s)
                .Count(localeHints.Contains));
    }

    internal static IReadOnlySet<string> Tokens(string path) =>
        path.ToLowerInvariant().Split(['/', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    [GeneratedRegex(@"(?i)(image|video)s?[^/]*$")]
    private static partial Regex MediaSitemap();

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

    internal static string Decode(byte[] bytes)
    {
        string text;
        if (bytes is [0x1f, 0x8b, ..])
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        else
        {
            text = Encoding.UTF8.GetString(bytes);
        }

        var start = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (start.Length == 0)
        {
            throw new InvalidDataException("empty response");
        }

        var head = start[..Math.Min(start.Length, 500)];
        if (head.Contains("<html", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("got a web page instead of a sitemap (often a bot check)");
        }

        return text;
    }
}
