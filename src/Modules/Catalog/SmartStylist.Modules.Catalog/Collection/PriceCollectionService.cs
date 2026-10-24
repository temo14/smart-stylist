using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>Runs one shop's daily collection and stores products, prices and a run record.</summary>
public sealed class PriceCollectionService
{
    private static readonly CollectorStrategy[] AutoOrder =
        [CollectorStrategy.Shopify, CollectorStrategy.SitemapJsonLd, CollectorStrategy.BrowserJsonLd];

    private readonly CatalogDbContext _db;
    private readonly IReadOnlyDictionary<CollectorStrategy, IShopCollector> _collectors;
    private readonly IOptions<CatalogOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PriceCollectionService> _logger;

    internal PriceCollectionService(
        CatalogDbContext db,
        IEnumerable<IShopCollector> collectors,
        IOptions<CatalogOptions> options,
        TimeProvider time,
        ILogger<PriceCollectionService> logger)
    {
        _db = db;
        _collectors = collectors.ToDictionary(c => c.Strategy);
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task<CollectionRun> CollectAsync(string shopKey, int? maxProducts, CancellationToken cancellationToken)
    {
        var shop = await _db.Shops.SingleOrDefaultAsync(s => s.Key == shopKey, cancellationToken)
            ?? throw new InvalidOperationException($"Unknown shop '{shopKey}'. Check Catalog:Shops in appsettings.json.");

        var run = new CollectionRun { Shop = shop, StartedAt = _time.GetUtcNow() };
        _db.CollectionRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);

        var shopOptions = _options.Value.Shops.FirstOrDefault(s => s.Key == shopKey);
        var limit = maxProducts ?? _options.Value.MaxProductsPerShop;
        // Revisit products priced in the last week first; ones gone longer make room for new sitemap URLs.
        var recent = _time.GetUtcNow().AddDays(-7);
        var knownUrls = await _db.Products
            .Where(p => p.ShopId == shop.Id && p.LastSeenAt >= recent)
            .OrderByDescending(p => p.LastSeenAt)
            .Select(p => p.Url)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var baseUrl = new Uri(shop.BaseUrl);
        var request = new ShopRequest(
            ShopRoot: new Uri(baseUrl.GetLeftPart(UriPartial.Authority)),
            BaseUrl: baseUrl,
            Currency: shop.Currency,
            SitemapUrl: shopOptions?.SitemapUrl,
            ProductUrlPattern: shopOptions?.ProductUrlPattern,
            KnownProductUrls: knownUrls,
            MaxProducts: limit);

        var errors = new List<string>();
        try
        {
            foreach (var strategy in StrategiesToTry(shop))
            {
                if (!_collectors.TryGetValue(strategy, out var collector))
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("Collecting {Shop} with {Strategy}", shop.Key, strategy);
                    var result = await collector.CollectAsync(request, cancellationToken);
                    if (result.Products.Count == 0)
                    {
                        errors.Add($"{strategy}: no products");
                        continue;
                    }

                    run.Strategy = strategy;
                    run.PagesFailed = result.FailedRequests;
                    if (shop.ConfiguredStrategy == CollectorStrategy.Auto)
                    {
                        shop.DetectedStrategy = strategy;
                    }

                    await SaveAsync(shop, run, result.Products, cancellationToken);
                    run.Status = RunStatus.Succeeded;
                    break;
                }
                catch (CollectorUnusableException ex)
                {
                    _logger.LogWarning("{Shop}: {Error}", shop.Key, ex.Message);
                    errors.Add(ex.Message);
                }
            }

            if (run.Status != RunStatus.Succeeded)
            {
                run.Status = RunStatus.Failed;
                if (shop.ConfiguredStrategy == CollectorStrategy.Auto)
                {
                    // Probe everything again tomorrow instead of retrying a strategy that stopped working.
                    shop.DetectedStrategy = null;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Collection for {Shop} crashed", shop.Key);
            foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.Entity is Product or PriceSnapshot).ToList())
            {
                entry.State = EntityState.Detached;
            }

            run.Status = RunStatus.Failed;
            errors.Add(ex.Message);
        }
        finally
        {
            run.FinishedAt = _time.GetUtcNow();
            run.Error = errors.Count == 0 ? null : Truncate(string.Join(" | ", errors), 4000);
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        _logger.LogInformation(
            "{Shop}: {Status} via {Strategy}, {Products} products, {Snapshots} prices, {Failed} failed pages",
            shop.Key, run.Status, run.Strategy, run.ProductsSeen, run.SnapshotsWritten, run.PagesFailed);
        return run;
    }

    private static IEnumerable<CollectorStrategy> StrategiesToTry(Shop shop)
    {
        if (shop.ConfiguredStrategy != CollectorStrategy.Auto)
        {
            return [shop.ConfiguredStrategy];
        }

        return shop.DetectedStrategy is { } detected
            ? AutoOrder.Where(s => s != detected).Prepend(detected)
            : AutoOrder;
    }

    private async Task SaveAsync(Shop shop, CollectionRun run, IReadOnlyList<ScrapedProduct> scraped, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var unique = scraped.GroupBy(p => p.ExternalId).Select(g => g.First()).ToList();
        var externalIds = unique.Select(p => p.ExternalId).ToList();
        var existing = await _db.Products
            .Where(p => p.ShopId == shop.Id && externalIds.Contains(p.ExternalId))
            .ToDictionaryAsync(p => p.ExternalId, cancellationToken);

        var existingIds = existing.Values.Select(p => p.Id).ToList();
        var todaysSnapshots = await _db.PriceSnapshots
            .Where(s => s.ObservedOn == today && existingIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => (s.ProductId, s.VariantKey), cancellationToken);

        foreach (var item in unique)
        {
            if (!existing.TryGetValue(item.ExternalId, out var product))
            {
                product = new Product
                {
                    ShopId = shop.Id,
                    ExternalId = Truncate(item.ExternalId, 500),
                    Url = item.Url,
                    Title = item.Title,
                    FirstSeenAt = now,
                };
                _db.Products.Add(product);
            }

            product.Url = Truncate(item.Url, 2000);
            product.Title = Truncate(item.Title, 500);
            product.Brand = item.Brand is null ? null : Truncate(item.Brand, 200);
            product.ImageUrl = item.ImageUrl is null || item.ImageUrl.Length > 2000 ? null : item.ImageUrl;
            product.LastSeenAt = now;

            foreach (var offer in item.Offers.GroupBy(o => o.VariantKey).Select(g => g.First()))
            {
                var variantKey = Truncate(offer.VariantKey, 200);
                if (product.Id == 0 || !todaysSnapshots.TryGetValue((product.Id, variantKey), out var snapshot))
                {
                    snapshot = new PriceSnapshot { Product = product, VariantKey = variantKey, Currency = offer.Currency, ObservedOn = today };
                    _db.PriceSnapshots.Add(snapshot);
                }

                snapshot.SizeLabel = offer.SizeLabel is null ? null : Truncate(offer.SizeLabel, 100);
                snapshot.Price = offer.Price;
                snapshot.ListPrice = offer.ListPrice;
                snapshot.Currency = offer.Currency.Length == 3 ? offer.Currency : shop.Currency;
                snapshot.InStock = offer.InStock;
                snapshot.CapturedAt = now;
                run.SnapshotsWritten++;
            }

            run.ProductsSeen++;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
