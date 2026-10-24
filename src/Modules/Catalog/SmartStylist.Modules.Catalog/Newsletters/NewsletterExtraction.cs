using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>What the model read from one email. Prices are strings exactly as printed; code parses them.</summary>
public sealed record NewsletterExtraction(
    [property: JsonPropertyName("has_product_prices")] bool HasProductPrices,
    [property: JsonPropertyName("sale_ends_on")] string? SaleEndsOn,
    [property: JsonPropertyName("promo_code")] string? PromoCode,
    [property: JsonPropertyName("products")] IReadOnlyList<ExtractedProduct> Products)
{
    public static readonly NewsletterExtraction Empty = new(false, null, null, []);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static NewsletterExtraction Parse(string json) =>
        JsonSerializer.Deserialize<NewsletterExtraction>(json, JsonOptions)
        ?? throw new JsonException("The model returned an empty JSON value.");

    /// <summary>JSON schema sent to the API as the structured output format.</summary>
    internal static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "has_product_prices", "sale_ends_on", "promo_code", "products" }),
        ["properties"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["has_product_prices"] = new { type = "boolean" },
            ["sale_ends_on"] = NullableString("Last sale day as YYYY-MM-DD, only if the email states it."),
            ["promo_code"] = NullableString(null),
            ["products"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "title", "link_id", "price", "list_price", "currency" },
                    properties = new Dictionary<string, object>
                    {
                        ["title"] = new { type = "string" },
                        ["link_id"] = NullableString("Id like L12 of the link to this product."),
                        ["price"] = new { type = "string", description = "Price the buyer pays, copied exactly as printed, e.g. \"$59.40\"." },
                        ["list_price"] = NullableString("Crossed-out original price exactly as printed, or null."),
                        ["currency"] = NullableString("ISO 4217 code if clear from the email, e.g. USD."),
                    },
                },
            },
        }),
    };

    private static object NullableString(string? description) => description is null
        ? new { anyOf = new object[] { new { type = "string" }, new { type = "null" } } }
        : new { anyOf = new object[] { new { type = "string" }, new { type = "null" } }, description };
}

public sealed record ExtractedProduct(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("link_id")] string? LinkId,
    [property: JsonPropertyName("price")] string Price,
    [property: JsonPropertyName("list_price")] string? ListPrice,
    [property: JsonPropertyName("currency")] string? Currency);

public sealed record NewsletterInput(string ShopName, string Subject, DateTimeOffset ReceivedAt, string Text);

public interface INewsletterExtractor
{
    Task<NewsletterExtraction> ExtractAsync(NewsletterInput input, CancellationToken cancellationToken);
}
