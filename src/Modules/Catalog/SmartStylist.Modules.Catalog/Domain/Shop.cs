namespace SmartStylist.Modules.Catalog.Domain;

public sealed class Shop
{
    public int Id { get; set; }

    /// <summary>Stable short key used in config, jobs and URLs, e.g. "levis".</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public required string BaseUrl { get; set; }

    /// <summary>ISO 4217 currency assumed when the shop data does not state one.</summary>
    public required string Currency { get; set; }

    public CollectorStrategy ConfiguredStrategy { get; set; } = CollectorStrategy.Auto;

    /// <summary>The strategy that last worked when <see cref="ConfiguredStrategy"/> is Auto.</summary>
    public CollectorStrategy? DetectedStrategy { get; set; }

    public bool Enabled { get; set; } = true;
}
