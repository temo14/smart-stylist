using Microsoft.EntityFrameworkCore;
using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Reporting;

public enum DataGrade
{
    NotRunYet,
    Failing,
    Poor,
    Usable,
    Clean,
}

public sealed record ShopQuality(
    string ShopKey,
    string ShopName,
    bool Enabled,
    CollectorStrategy? Strategy,
    DateTimeOffset? LastRunAt,
    RunStatus? LastRunStatus,
    string? LastError,
    int ProductsTracked,
    int ProductsLastRun,
    double PageSuccessRate,
    int PricesLatestDay,
    double SizeCoverage,
    double StockCoverage,
    double OnSaleShare,
    int DaysOfHistory,
    int ProductsWithPriceChange,
    DataGrade Grade);

/// <summary>
/// Answers the M0 question: which shops give us clean daily price data, and which need more work.
/// </summary>
public sealed class DataQualityReport(CatalogDbContext db)
{
    public async Task<IReadOnlyList<ShopQuality>> BuildAsync(CancellationToken cancellationToken)
    {
        var shops = await db.Shops.AsNoTracking().OrderBy(s => s.Key).ToListAsync(cancellationToken);
        var rows = new List<ShopQuality>();

        foreach (var shop in shops)
        {
            var lastRun = await db.CollectionRuns.AsNoTracking()
                .Where(r => r.ShopId == shop.Id && r.Status != RunStatus.Running)
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var snapshots = db.PriceSnapshots.AsNoTracking().Where(s => s.Product.ShopId == shop.Id);
            var latestDay = await snapshots.MaxAsync(s => (DateOnly?)s.ObservedOn, cancellationToken);
            var latest = latestDay is null
                ? []
                : await snapshots.Where(s => s.ObservedOn == latestDay)
                    .Select(s => new { HasSize = s.SizeLabel != null, HasStock = s.InStock != null, OnSale = s.ListPrice != null })
                    .ToListAsync(cancellationToken);

            var days = await snapshots.Select(s => s.ObservedOn).Distinct().CountAsync(cancellationToken);
            var changed = await snapshots
                .Select(s => new { s.ProductId, s.VariantKey, s.Price })
                .Distinct()
                .GroupBy(s => new { s.ProductId, s.VariantKey })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.ProductId)
                .Distinct()
                .CountAsync(cancellationToken);
            var tracked = await db.Products.CountAsync(p => p.ShopId == shop.Id, cancellationToken);

            double Share(int count) => latest.Count == 0 ? 0 : (double)count / latest.Count;
            var attempted = (lastRun?.ProductsSeen ?? 0) + (lastRun?.PagesFailed ?? 0);
            var successRate = attempted == 0 ? 0 : (double)lastRun!.ProductsSeen / attempted;
            var sizeCoverage = Share(latest.Count(s => s.HasSize));

            rows.Add(new ShopQuality(
                ShopKey: shop.Key,
                ShopName: shop.Name,
                Enabled: shop.Enabled,
                Strategy: lastRun?.Strategy ?? shop.DetectedStrategy,
                LastRunAt: lastRun?.StartedAt,
                LastRunStatus: lastRun?.Status,
                LastError: lastRun?.Error,
                ProductsTracked: tracked,
                ProductsLastRun: lastRun?.ProductsSeen ?? 0,
                PageSuccessRate: successRate,
                PricesLatestDay: latest.Count,
                SizeCoverage: sizeCoverage,
                StockCoverage: Share(latest.Count(s => s.HasStock)),
                OnSaleShare: Share(latest.Count(s => s.OnSale)),
                DaysOfHistory: days,
                ProductsWithPriceChange: changed,
                Grade: Grade(lastRun, successRate, sizeCoverage)));
        }

        return rows;
    }

    /// <summary>
    /// Clean: almost every page gives a price and most prices have a size, so verdicts can be per size.
    /// Usable: prices work but sizes or some pages are missing. Poor: data is too patchy to trust.
    /// </summary>
    internal static DataGrade Grade(CollectionRun? lastRun, double pageSuccessRate, double sizeCoverage) => lastRun switch
    {
        null => DataGrade.NotRunYet,
        { Status: RunStatus.Failed } or { ProductsSeen: 0 } => DataGrade.Failing,
        _ when pageSuccessRate >= 0.9 && sizeCoverage >= 0.5 => DataGrade.Clean,
        _ when pageSuccessRate >= 0.6 => DataGrade.Usable,
        _ => DataGrade.Poor,
    };
}
