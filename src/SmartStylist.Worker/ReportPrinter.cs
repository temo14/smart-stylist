using SmartStylist.Modules.Catalog.Reporting;

namespace SmartStylist.Worker;

internal static class ReportPrinter
{
    public static void Print(IReadOnlyList<ShopQuality> rows, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"{"Shop",-16} {"Grade",-9} {"Strategy",-14} {"Products",8} {"Pages ok",8} {"Sizes",6} {"Stock",6} {"On sale",7} {"Days",4} {"Moved",5}");
        output.WriteLine(new string('-', 92));
        foreach (var row in rows.OrderByDescending(r => r.Grade).ThenBy(r => r.ShopKey))
        {
            output.WriteLine(
                $"{row.ShopKey,-16} {row.Grade,-9} {row.Strategy?.ToString() ?? "-",-14} {row.ProductsTracked,8} " +
                $"{Percent(row.PageSuccessRate),8} {Percent(row.SizeCoverage),6} {Percent(row.StockCoverage),6} " +
                $"{Percent(row.OnSaleShare),7} {row.DaysOfHistory,4} {row.ProductsWithPriceChange,5}");
        }

        var failing = rows.Where(r => r.Grade is DataGrade.Failing or DataGrade.Poor && r.LastError is not null).ToList();
        if (failing.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Why shops failed:");
            foreach (var row in failing)
            {
                output.WriteLine($"  {row.ShopKey}: {row.LastError}");
            }
        }

        output.WriteLine();
        output.WriteLine("Pages ok = product pages that gave a price. Sizes/Stock = share of prices with a size/stock flag.");
        output.WriteLine("On sale = share with a crossed-out price. Moved = products whose price changed across days.");
    }

    private static string Percent(double share) => $"{share:P0}";
}
