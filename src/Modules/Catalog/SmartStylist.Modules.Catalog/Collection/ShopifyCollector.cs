using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartStylist.Modules.Catalog.Domain;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>Reads Shopify's public /products.json feed: every product and size with price and "compare at" price.</summary>
internal sealed class ShopifyCollector(HttpPageFetcher fetcher, IOptions<CatalogOptions> options) : IShopCollector
{
    private const int PageSize = 250;

    public CollectorStrategy Strategy => CollectorStrategy.Shopify;

    public async Task<CollectorResult> CollectAsync(ShopRequest request, CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        for (var page = 1; products.Count < request.MaxProducts; page++)
        {
            var url = new Uri(request.ShopRoot, $"/products.json?limit={PageSize}&page={page}");
            IReadOnlyList<ScrapedProduct> batch;
            try
            {
                var json = await fetcher.GetPageAsync(url, cancellationToken);
                batch = ShopifyFeedParser.Parse(json, request.ShopRoot, request.Currency);
            }
            catch (Exception ex) when (page == 1 && ex is HttpRequestException or JsonException or FormatException)
            {
                throw new CollectorUnusableException($"Shopify: no feed at {url} ({SitemapReader.FirstLine(ex.Message)})", ex);
            }

            if (batch.Count == 0)
            {
                break;
            }

            products.AddRange(batch);
            await Task.Delay(options.Value.RequestDelayMs, cancellationToken);
        }

        return new CollectorResult(products.Take(request.MaxProducts).ToList(), FailedRequests: 0);
    }
}
