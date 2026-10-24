using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>Maps one page of a Shopify /products.json feed to scraped products.</summary>
internal static partial class ShopifyFeedParser
{
    [GeneratedRegex("size|waist|length|inseam|width", RegexOptions.IgnoreCase)]
    private static partial Regex SizeOptionName();

    public static IReadOnlyList<ScrapedProduct> Parse(string json, Uri shopRoot, string currency)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Not a Shopify products feed: no 'products' array.");
        }

        var result = new List<ScrapedProduct>();
        foreach (var product in products.EnumerateArray())
        {
            var mapped = MapProduct(product, shopRoot, currency);
            if (mapped is not null)
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    private static ScrapedProduct? MapProduct(JsonElement product, Uri shopRoot, string currency)
    {
        var id = ReadId(product, "id");
        var handle = ReadString(product, "handle");
        var title = ReadString(product, "title");
        if (id is null || handle is null || title is null)
        {
            return null;
        }

        var sizeOptionPositions = new List<int>();
        if (product.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var option in options.EnumerateArray())
            {
                index++;
                var name = ReadString(option, "name");
                var position = option.TryGetProperty("position", out var p) && p.TryGetInt32(out var pos) ? pos : index;
                if (name is not null && SizeOptionName().IsMatch(name))
                {
                    sizeOptionPositions.Add(position);
                }
            }
        }

        var offers = new List<ScrapedOffer>();
        if (product.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array)
        {
            foreach (var variant in variants.EnumerateArray())
            {
                var price = variant.TryGetProperty("price", out var priceElement) ? PriceParser.Parse(priceElement) : null;
                var variantId = ReadId(variant, "id");
                if (price is null || variantId is null)
                {
                    continue;
                }

                var compareAt = variant.TryGetProperty("compare_at_price", out var compareElement)
                    ? PriceParser.Parse(compareElement)
                    : null;

                bool? available = variant.TryGetProperty("available", out var availableElement)
                    && availableElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? availableElement.GetBoolean()
                        : null;

                offers.Add(new ScrapedOffer(
                    VariantKey: variantId,
                    SizeLabel: SizeLabel(variant, sizeOptionPositions),
                    Price: price.Value,
                    ListPrice: compareAt > price ? compareAt : null,
                    Currency: currency,
                    InStock: available));
            }
        }

        string? image = null;
        if (product.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            image = images.EnumerateArray().Select(i => ReadString(i, "src")).FirstOrDefault(s => s is not null);
        }

        return new ScrapedProduct(
            ExternalId: id,
            Url: new Uri(shopRoot, $"/products/{handle}").ToString(),
            Title: title,
            Brand: ReadString(product, "vendor"),
            ImageUrl: image,
            Offers: offers);
    }

    private static string? SizeLabel(JsonElement variant, List<int> sizeOptionPositions)
    {
        if (sizeOptionPositions.Count > 0)
        {
            var parts = sizeOptionPositions
                .Select(position => ReadString(variant, $"option{position}"))
                .Where(value => value is not null);
            var label = string.Join(" / ", parts);
            return label.Length > 0 ? label : null;
        }

        var title = ReadString(variant, "title");
        return title is null || title.Equals("Default Title", StringComparison.OrdinalIgnoreCase) ? null : title;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string? ReadId(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number => value.GetInt64().ToString(CultureInfo.InvariantCulture),
                JsonValueKind.String => value.GetString(),
                _ => null,
            }
            : null;
}
