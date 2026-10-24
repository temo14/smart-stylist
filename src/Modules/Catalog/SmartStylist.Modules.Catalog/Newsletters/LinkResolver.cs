using Microsoft.Extensions.Logging;

namespace SmartStylist.Modules.Catalog.Newsletters;

public interface ILinkResolver
{
    /// <summary>Follows an email's tracking link to the shop page and returns that page's URL without query.</summary>
    Task<Uri?> ResolveAsync(string link, ShopDomains shop, CancellationToken cancellationToken);
}

/// <summary>
/// Follows redirects one hop at a time and stops as soon as a hop points at the shop, so the shop
/// page itself is never downloaded (most shops block automated visits, the tracking servers do not).
/// </summary>
internal sealed class LinkResolver(HttpClient httpClient, ILogger<LinkResolver> logger) : ILinkResolver
{
    public const string ClientName = "newsletter-links";
    private const int MaxHops = 8;

    public async Task<Uri?> ResolveAsync(string link, ShopDomains shop, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var current))
        {
            return null;
        }

        try
        {
            for (var hop = 0; hop < MaxHops; hop++)
            {
                if (shop.IsShopPage(current.Host))
                {
                    return Canonical(current);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is < 300 or > 399 || response.Headers.Location is not { } location)
                {
                    return null;
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Could not follow {Link}", link);
        }

        return null;
    }

    public static Uri Canonical(Uri url)
    {
        var builder = new UriBuilder(url) { Query = "", Fragment = "", Scheme = Uri.UriSchemeHttps, Port = -1 };
        builder.Host = builder.Host.ToLowerInvariant();
        if (builder.Path.Length > 1)
        {
            builder.Path = builder.Path.TrimEnd('/');
        }

        return builder.Uri;
    }
}
