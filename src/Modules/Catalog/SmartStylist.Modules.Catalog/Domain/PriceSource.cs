namespace SmartStylist.Modules.Catalog.Domain;

/// <summary>Where a price snapshot came from.</summary>
public enum PriceSource
{
    /// <summary>Read from the shop's own site or feed by a collector.</summary>
    Crawl,

    /// <summary>Read from a sale email the shop sent.</summary>
    Newsletter,
}
