using System.Globalization;
using System.Text.Json;

namespace SmartStylist.Modules.Catalog.Collection;

internal static class PriceParser
{
    /// <summary>
    /// Parses prices as shops write them: 59.4, "59.40", "1,299.00", "59,40", "1.299,00", "$59.40".
    /// Returns null for anything that is not a positive amount.
    /// </summary>
    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());
        if (text.Length == 0)
        {
            return null;
        }

        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');

        if (lastDot >= 0 && lastComma >= 0)
        {
            // The separator that comes last is the decimal one.
            text = lastComma > lastDot
                ? text.Replace(".", "").Replace(',', '.')
                : text.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            // "59,40" is a decimal comma; "1,299" is a thousands separator.
            var decimals = text.Length - lastComma - 1;
            text = decimals is 1 or 2 && text.Count(c => c == ',') == 1
                ? text.Replace(',', '.')
                : text.Replace(",", "");
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0
            ? decimal.Round(value, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    public static decimal? Parse(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number when element.TryGetDecimal(out var number) && number > 0
            => decimal.Round(number, 2, MidpointRounding.AwayFromZero),
        JsonValueKind.String => Parse(element.GetString()),
        _ => null,
    };
}
