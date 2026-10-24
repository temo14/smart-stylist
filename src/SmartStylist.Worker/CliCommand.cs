using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Modules.Catalog.Persistence;
using SmartStylist.Modules.Catalog.Reporting;

namespace SmartStylist.Worker;

internal sealed record CliCommand(string Name, IReadOnlyList<string> ShopKeys, int? Limit, bool UseBrowser)
{
    private static readonly string[] Commands = ["collect", "report", "migrate", "install-browser", "probe"];

    public static CliCommand? Parse(string[] args)
    {
        if (args.Length == 0 || !Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var shops = new List<string>();
        int? limit = null;
        var useBrowser = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] is "--limit" && i + 1 < args.Length)
            {
                limit = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] is "--browser")
            {
                useBrowser = true;
            }
            else if (!args[i].StartsWith('-'))
            {
                shops.Add(args[i]);
            }
        }

        return new CliCommand(args[0].ToLowerInvariant(), shops, limit, useBrowser);
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
        var runLog = provider.GetRequiredService<RunLogFile>();

        if (Name == "probe")
        {
            return await ProbeAsync(provider.GetRequiredService<ProductPageProbe>(), cancellationToken);
        }

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
                var line = $"{key,-16} {run.Status,-10} {run.Strategy?.ToString() ?? "-",-14} {run.ProductsSeen,5} products {run.SnapshotsWritten,6} prices";
                Console.WriteLine(line);
                runLog.WriteLine(line);
            }
        }

        if (Name is "collect" or "report")
        {
            var report = await provider.GetRequiredService<DataQualityReport>().BuildAsync(cancellationToken);
            var text = new StringWriter();
            ReportPrinter.Print(report, text);
            Console.Write(text);
            runLog.WriteLine($"Report:{Environment.NewLine}{text}");
        }

        Console.WriteLine($"Run log: {runLog.FilePath}");
        return 0;
    }

    // Arguments are product URLs here, not shop keys.
    private async Task<int> ProbeAsync(ProductPageProbe probe, CancellationToken cancellationToken)
    {
        if (ShopKeys.Count == 0)
        {
            Console.WriteLine("Usage: probe <product url> [--browser]");
            return 1;
        }

        var url = new Uri(ShopKeys[0]);
        var result = await probe.ProbeAsync(url, UseBrowser, "USD", cancellationToken);

        if (result.Html is not null)
        {
            var file = Path.GetFullPath($"probe-{url.Host}.html");
            await File.WriteAllTextAsync(file, result.Html, cancellationToken);
            Console.WriteLine($"Saved the page to {file}");
        }

        if (result.Product is not { } product)
        {
            Console.WriteLine($"No price found: {result.Error}");
            return 1;
        }

        Console.WriteLine($"{product.Title} | brand: {product.Brand ?? "-"} | id: {product.ExternalId}");
        foreach (var offer in product.Offers.Take(20))
        {
            Console.WriteLine($"  {offer.SizeLabel ?? "(no size)",-14} {offer.Price,9:0.00} {offer.Currency}" +
                $"{(offer.ListPrice is { } was ? $"  was {was:0.00}" : "")}  in stock: {offer.InStock?.ToString() ?? "?"}");
        }

        return 0;
    }
}
