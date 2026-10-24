namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>A product as read from a shop, before it is matched to the database.</summary>
public sealed record ScrapedProduct(
    string ExternalId,
    string Url,
    string Title,
    string? Brand,
    string? ImageUrl,
    IReadOnlyList<ScrapedOffer> Offers);

/// <summary>A sellable variant (usually a size) and its price today.</summary>
public sealed record ScrapedOffer(
    string VariantKey,
    string? SizeLabel,
    decimal Price,
    decimal? ListPrice,
    string Currency,
    bool? InStock);
