using Hangfire;
using Microsoft.Extensions.Options;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>Hangfire entry point: reads the configured mailbox and prices new sale emails.</summary>
public sealed class ImportNewslettersJob(NewsletterImportService importer, IOptions<NewsletterOptions> options)
{
    public const string RecurringJobId = "import-newsletters";

    [AutomaticRetry(Attempts = 1)]
    [DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        importer.ImportAsync(new ImapMailSource(options.Value.Imap), cancellationToken);
}
