using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartStylist.Modules.Catalog;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Worker;

/// <summary>Migrates the database, syncs shops from config and keeps one daily Hangfire job per enabled shop.</summary>
internal sealed class RecurringJobsSetup(
    IServiceScopeFactory scopes,
    IRecurringJobManager recurringJobs,
    IOptions<CatalogOptions> options,
    ILogger<RecurringJobsSetup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
        var shops = await scope.ServiceProvider.GetRequiredService<ShopCatalogSync>().SyncAsync(cancellationToken);

        foreach (var shop in shops)
        {
            var jobId = CollectPricesJob.RecurringJobId(shop.Key);
            if (shop.Enabled)
            {
                recurringJobs.AddOrUpdate<CollectPricesJob>(
                    jobId,
                    job => job.RunAsync(shop.Key, CancellationToken.None),
                    options.Value.DailyCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
            }
            else
            {
                recurringJobs.RemoveIfExists(jobId);
            }
        }

        logger.LogInformation("Scheduled daily price collection for {Count} shops ({Cron} UTC)",
            shops.Count(s => s.Enabled), options.Value.DailyCron);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
