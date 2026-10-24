namespace SmartStylist.Modules.Catalog.Newsletters;

public interface IMailSource
{
    /// <summary>Human-readable name for logs, e.g. "imap.gmail.com/Sales".</summary>
    string Description { get; }

    IAsyncEnumerable<InboxMessage> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
