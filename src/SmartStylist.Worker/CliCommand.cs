using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Modules.Catalog.Persistence;
using SmartStylist.Modules.Catalog.Reporting;

namespace SmartStylist.Worker;

internal sealed record CliCommand(string Name, IReadOnlyList<string> ShopKeys, int? Limit)
{
    private static readonly string[] Commands = ["collect", "report", "migrate", "install-browser"];

    public static CliCommand? Parse(string[] args)
    {
        if (args.Length == 0 || !Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var shops = new List<string>();
        int? limit = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] is "--limit" && i + 1 < args.Length)
            {
                limit = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (!args[i].StartsWith('-'))
            {
                shops.Add(args[i]);
            }
        }

        return new CliCommand(args[0].ToLowerInvariant(), shops, limit);
    }

    public async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (Name == "install-browser")
        {
            // Downloads the Chromium build that matches the Microsoft.Playwright package. No PowerShell needed.
            return Microsoft.Playwright.Program.Main(["install", "chromium"]);
        }

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        if (Name is "migrate" or "collect")
        {
            await provider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
            await provider.GetRequiredService<ShopCatalogSync>().SyncAsync(cancellationToken);
        }

        if (Name == "collect")
        {
            var db = provider.GetRequiredService<CatalogDbContext>();
            var keys = ShopKeys.Count > 0
                ? ShopKeys
                : await db.Shops.Where(s => s.Enabled).OrderBy(s => s.Key).Select(s => s.Key).ToListAsync(cancellationToken);

            foreach (var key in keys)
            {
                // A fresh scope per shop keeps each DbContext small and isolated.
                await using var shopScope = services.CreateAsyncScope();
                var run = await shopScope.ServiceProvider.GetRequiredService<PriceCollectionService>()
                    .CollectAsync(key, Limit, cancellationToken);
                Console.WriteLine($"{key,-16} {run.Status,-10} {run.Strategy?.ToString() ?? "-",-14} {run.ProductsSeen,5} products {run.SnapshotsWritten,6} prices");
            }
        }

        if (Name is "collect" or "report")
        {
            var report = await provider.GetRequiredService<DataQualityReport>().BuildAsync(cancellationToken);
            ReportPrinter.Print(report, Console.Out);
        }

        return 0;
    }
}
