using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>Makes the Shops table match Catalog:Shops in configuration. Shops removed from config are disabled, not deleted.</summary>
public sealed class ShopCatalogSync(CatalogDbContext db, IOptions<CatalogOptions> options)
{
    public async Task<IReadOnlyList<Shop>> SyncAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value.Shops;
        var shops = await db.Shops.ToDictionaryAsync(s => s.Key, cancellationToken);

        foreach (var config in configured)
        {
            if (!shops.TryGetValue(config.Key, out var shop))
            {
                shop = new Shop { Key = config.Key, Name = config.Name, BaseUrl = config.BaseUrl, Currency = config.Currency };
                db.Shops.Add(shop);
                shops[config.Key] = shop;
            }

            if (shop.ConfiguredStrategy != config.Strategy || shop.BaseUrl != config.BaseUrl)
            {
                shop.DetectedStrategy = null;
            }

            shop.Name = config.Name;
            shop.BaseUrl = config.BaseUrl;
            shop.Currency = config.Currency.ToUpperInvariant();
            shop.ConfiguredStrategy = config.Strategy;
            shop.Enabled = config.Enabled;
        }

        var configuredKeys = configured.Select(c => c.Key).ToHashSet();
        foreach (var removed in shops.Values.Where(s => !configuredKeys.Contains(s.Key)))
        {
            removed.Enabled = false;
        }

        await db.SaveChangesAsync(cancellationToken);
        return shops.Values.OrderBy(s => s.Key).ToList();
    }
}
