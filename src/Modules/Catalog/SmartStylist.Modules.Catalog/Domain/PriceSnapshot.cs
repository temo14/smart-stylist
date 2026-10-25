namespace SmartStylist.Modules.Catalog.Domain;

/// <summary>
/// One price per product variant per day. A second capture on the same day overwrites the first.
/// </summary>
public sealed class PriceSnapshot
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>Variant id or SKU; empty when the shop only exposes a product-level price.</summary>
    public required string VariantKey { get; set; }

    public string? SizeLabel { get; set; }

    public DateOnly ObservedOn { get; set; }

    /// <summary>The price a buyer would pay today, before shipping and duties.</summary>
    public decimal Price { get; set; }

    /// <summary>The crossed-out "was" price, when the shop shows one.</summary>
    public decimal? ListPrice { get; set; }

    public required string Currency { get; set; }

    public bool? InStock { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public PriceSource Source { get; set; } = PriceSource.Crawl;
}
