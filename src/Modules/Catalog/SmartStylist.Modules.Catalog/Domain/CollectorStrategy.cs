namespace SmartStylist.Modules.Catalog.Domain;

public enum CollectorStrategy
{
    /// <summary>Try the cheapest source first and remember what worked.</summary>
    Auto,

    /// <summary>Public Shopify feed at /products.json.</summary>
    Shopify,

    /// <summary>Product URLs from the sitemap, prices from schema.org JSON-LD over plain HTTP.</summary>
    SitemapJsonLd,

    /// <summary>Same as <see cref="SitemapJsonLd"/>, but pages are rendered in a headless browser.</summary>
    BrowserJsonLd,
}
