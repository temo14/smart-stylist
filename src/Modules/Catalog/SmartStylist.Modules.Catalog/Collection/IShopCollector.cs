using SmartStylist.Modules.Catalog.Domain;

namespace SmartStylist.Modules.Catalog.Collection;

internal interface IShopCollector
{
    CollectorStrategy Strategy { get; }

    /// <summary>
    /// Reads up to <see cref="ShopRequest.MaxProducts"/> products. Throws <see cref="CollectorUnusableException"/>
    /// when this strategy clearly does not work for the shop, so Auto can move on to the next one.
    /// </summary>
    Task<CollectorResult> CollectAsync(ShopRequest request, CancellationToken cancellationToken);
}

internal sealed record ShopRequest(
    Uri ShopRoot,
    Uri BaseUrl,
    string Currency,
    string? SitemapUrl,
    string? ProductUrlPattern,
    IReadOnlyList<string> KnownProductUrls,
    int MaxProducts);

internal sealed record CollectorResult(IReadOnlyList<ScrapedProduct> Products, int FailedRequests);

internal sealed class CollectorUnusableException(string message, Exception? inner = null) : Exception(message, inner);
