using Hangfire;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>Hangfire entry point. One recurring job per shop, so one blocked shop never delays the others.</summary>
public sealed class CollectPricesJob(PriceCollectionService collector)
{
    public static string RecurringJobId(string shopKey) => $"collect-prices:{shopKey}";

    [AutomaticRetry(Attempts = 1)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public Task RunAsync(string shopKey, CancellationToken cancellationToken) =>
        collector.CollectAsync(shopKey, maxProducts: null, cancellationToken);
}
