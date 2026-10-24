namespace SmartStylist.Modules.Catalog.Domain;

/// <summary>One sale email that was read, so the same email is never priced twice.</summary>
public sealed class NewsletterEmail
{
    public long Id { get; set; }

    /// <summary>The email's Message-ID header, or a hash of sender, date and subject when it has none.</summary>
    public required string MessageId { get; set; }

    public required string From { get; set; }

    public required string Subject { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public int? ShopId { get; set; }

    public Shop? Shop { get; set; }

    public NewsletterStatus Status { get; set; }

    /// <summary>Last day of the sale when the email states one.</summary>
    public DateOnly? SaleEndsOn { get; set; }

    public string? PromoCode { get; set; }

    /// <summary>Products the model found in the email, before validation.</summary>
    public int ProductsFound { get; set; }

    /// <summary>Products dropped because a price did not check out against the email text.</summary>
    public int ProductsRejected { get; set; }

    public int PricesWritten { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

public enum NewsletterStatus
{
    /// <summary>Prices were saved.</summary>
    Priced,

    /// <summary>Read fine, but the email has no product prices (e.g. a brand story or a sitewide code only).</summary>
    NoPrices,

    /// <summary>The sender does not match any shop in Catalog:Shops.</summary>
    UnknownSender,

    Failed,
}
