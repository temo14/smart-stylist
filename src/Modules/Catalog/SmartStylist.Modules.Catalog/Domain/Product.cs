namespace SmartStylist.Modules.Catalog.Domain;

public sealed class Product
{
    public long Id { get; set; }

    public int ShopId { get; set; }

    public Shop Shop { get; set; } = null!;

    /// <summary>The shop's own product id, or the canonical URL when the shop has none.</summary>
    public required string ExternalId { get; set; }

    public required string Url { get; set; }

    public required string Title { get; set; }

    public string? Brand { get; set; }

    public string? ImageUrl { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public List<PriceSnapshot> Snapshots { get; set; } = [];
}
