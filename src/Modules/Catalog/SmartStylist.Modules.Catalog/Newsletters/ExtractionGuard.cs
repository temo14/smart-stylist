using System.Globalization;
using System.Text.RegularExpressions;
using SmartStylist.Modules.Catalog.Collection;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>A newsletter price that passed every check and can be stored.</summary>
public sealed record NewsletterOffer(string Title, string? LinkUrl, decimal Price, decimal? ListPrice, string Currency);

/// <summary>
/// Deterministic checks on what the model returned. Money is never taken on trust: a price is kept
/// only if the same amount is printed in the email text.
/// </summary>
internal static partial class ExtractionGuard
{
    private const decimal MaxPrice = 100_000m;

    public static (IReadOnlyList<NewsletterOffer> Kept, int Rejected) Check(
        NewsletterExtraction extraction, NewsletterText email, string shopCurrency)
    {
        var kept = new List<NewsletterOffer>();
        var rejected = 0;
        var haystack = Squash(email.Text);

        foreach (var item in extraction.Products)
        {
            var title = item.Title?.Trim();
            var price = PriceParser.Parse(item.Price);
            if (string.IsNullOrEmpty(title) || price is null || price >= MaxPrice || !IsPrinted(item.Price, haystack))
            {
                rejected++;
                continue;
            }

            var listPrice = PriceParser.Parse(item.ListPrice);
            if (listPrice is not null && (listPrice <= price || listPrice >= MaxPrice || !IsPrinted(item.ListPrice!, haystack)))
            {
                listPrice = null;
            }

            kept.Add(new NewsletterOffer(title, email.LinkUrl(item.LinkId), price.Value, listPrice, Currency(item, shopCurrency)));
        }

        return (kept, rejected);
    }

    /// <summary>Parses the model's sale end date; anything that is not a plain ISO date is ignored.</summary>
    public static DateOnly? SaleEnd(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static bool IsPrinted(string price, string haystack)
    {
        // Compare the number as printed ("1,299.00", "59.40"), ignoring the currency symbol and spacing.
        var number = NumberRegex().Match(price);
        // The digits must stand alone, so "59" does not match inside "$159.00" or "59.99".
        return number.Success && Regex.IsMatch(haystack, $@"(?<![\d.,]){Regex.Escape(Squash(number.Value))}(?![\d]|[.,]\d)");
    }

    private static string Currency(ExtractedProduct item, string shopCurrency)
    {
        var symbol = item.Price.Contains('€') ? "EUR"
            : item.Price.Contains('£') ? "GBP"
            : item.Price.Contains('₾') ? "GEL"
            : null;
        if (symbol is not null)
        {
            return symbol;
        }

        var code = item.Currency?.Trim().ToUpperInvariant();
        return code is { Length: 3 } && code.All(char.IsAsciiLetterUpper) ? code : shopCurrency;
    }

    private static string Squash(string text) => WhitespaceRegex().Replace(text, "");

    [GeneratedRegex(@"\d[\d.,\s]*\d|\d")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
