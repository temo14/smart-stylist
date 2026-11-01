using Microsoft.EntityFrameworkCore;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Newsletters;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>A product page as seen in the user's own browser. Html only needs the page's structured data.</summary>
public sealed record PageCapture(string Url, string Html);

public enum PageCaptureStatus
{
    Saved,
    UnknownShop,
    NoPrice,
}

public sealed record CapturedSize(string Label, decimal Price, bool? InStock);

/// <summary>What the browser extension shows for a page. LowestPrice and DaysTracked cover every source of prices.</summary>
public sealed record PageCaptureResult(
    PageCaptureStatus Status,
    string? Shop = null,
    string? Title = null,
    decimal? Price = null,
    decimal? ListPrice = null,
    string? Currency = null,
    decimal? LowestPrice = null,
    int DaysTracked = 0,
    IReadOnlyList<CapturedSize>? Sizes = null);

/// <summary>
/// Stores the price of a product page the user opened. The shop sees a normal visitor, so this also works
/// for shops that block the collectors. The page is parsed the same way a crawled page is.
/// </summary>
public sealed class PageCaptureService
{
    private const int MaxUrl = 2000;

    private readonly CatalogDbContext _db;
    private readonly TimeProvider _time;

    internal PageCaptureService(CatalogDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<PageCaptureResult> CaptureAsync(PageCapture capture, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(capture.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return new PageCaptureResult(PageCaptureStatus.UnknownShop);
        }

        var shops = await _db.Shops.ToListAsync(cancellationToken);
        var shop = shops.FirstOrDefault(s => new ShopDomains(s.Key, s.BaseUrl, []).IsShopPage(url.Host));
        if (shop is null)
        {
            return new PageCaptureResult(PageCaptureStatus.UnknownShop);
        }

        var scraped = JsonLdProductParser.Parse(capture.Html, url, shop.Currency);
        if (scraped is null)
        {
            return new PageCaptureResult(PageCaptureStatus.NoPrice, shop.Name);
        }

        var product = await SaveAsync(shop, scraped, cancellationToken);

        var cheapest = scraped.Offers.MinBy(o => o.Price)!;
        var daily = await _db.PriceSnapshots
            .Where(s => s.ProductId == product.Id && s.Currency == cheapest.Currency)
            .GroupBy(s => s.ObservedOn)
            .Select(g => g.Min(s => s.Price))
            .ToListAsync(cancellationToken);

        return new PageCaptureResult(
            PageCaptureStatus.Saved,
            shop.Name,
            scraped.Title,
            cheapest.Price,
            cheapest.ListPrice,
            cheapest.Currency,
            daily.Min(),
            daily.Count,
            scraped.Offers.Where(o => o.SizeLabel is not null).Select(o => new CapturedSize(o.SizeLabel!, o.Price, o.InStock)).ToList());
    }

    private async Task<Product> SaveAsync(Shop shop, ScrapedProduct scraped, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // A sale email keys its products by page URL, so the same product may exist under that id.
        var product = await _db.Products.FirstOrDefaultAsync(p => p.ShopId == shop.Id && p.ExternalId == scraped.ExternalId, cancellationToken)
            ?? await _db.Products.FirstOrDefaultAsync(p => p.ShopId == shop.Id && (p.Url == scraped.Url || p.ExternalId == scraped.Url), cancellationToken);
        if (product is null)
        {
            product = new Product { ShopId = shop.Id, ExternalId = Truncate(scraped.ExternalId, 500), Url = "", Title = "", FirstSeenAt = now };
            _db.Products.Add(product);
        }

        product.Url = Truncate(scraped.Url, MaxUrl);
        product.Title = Truncate(scraped.Title, 500);
        product.Brand = scraped.Brand is null ? product.Brand : Truncate(scraped.Brand, 200);
        product.ImageUrl = scraped.ImageUrl is null || scraped.ImageUrl.Length > MaxUrl ? product.ImageUrl : scraped.ImageUrl;
        product.LastSeenAt = now;

        var todays = product.Id == 0
            ? []
            : await _db.PriceSnapshots.Where(s => s.ProductId == product.Id && s.ObservedOn == today).ToListAsync(cancellationToken);

        foreach (var offer in scraped.Offers.GroupBy(o => o.VariantKey).Select(g => g.First()))
        {
            var variantKey = Truncate(offer.VariantKey, 200);
            var snapshot = todays.FirstOrDefault(s => s.VariantKey == variantKey);
            if (snapshot is null)
            {
                snapshot = new PriceSnapshot { Product = product, VariantKey = variantKey, ObservedOn = today, Currency = offer.Currency };
                _db.PriceSnapshots.Add(snapshot);
            }

            snapshot.SizeLabel = offer.SizeLabel is null ? null : Truncate(offer.SizeLabel, 100);
            snapshot.Price = offer.Price;
            snapshot.ListPrice = offer.ListPrice;
            snapshot.Currency = offer.Currency.Length == 3 ? offer.Currency : shop.Currency;
            snapshot.InStock = offer.InStock;
            snapshot.CapturedAt = now;
            snapshot.Source = PriceSource.Extension;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return product;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
