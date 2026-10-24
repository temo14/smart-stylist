using System.Text.Json;
using AngleSharp.Html.Parser;

namespace SmartStylist.Modules.Catalog.Collection;

/// <summary>
/// Reads a product and its offers from a product page's schema.org JSON-LD, falling back to
/// Open Graph price meta tags. Most shop platforms publish one of the two for Google Shopping.
/// </summary>
internal static class JsonLdProductParser
{
    private static readonly HtmlParser HtmlParser = new();

    public static ScrapedProduct? Parse(string html, Uri pageUrl, string fallbackCurrency)
    {
        var document = HtmlParser.ParseDocument(html);
        var canonical = document.QuerySelector("link[rel=canonical]")?.GetAttribute("href");
        var productUrl = Uri.TryCreate(pageUrl, canonical, out var canonicalUri) ? canonicalUri : pageUrl;

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(script.TextContent, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            }
            catch (JsonException)
            {
                continue;
            }

            using (json)
            {
                var node = FindProductNode(json.RootElement);
                if (node is not null)
                {
                    var product = MapProduct(node.Value, productUrl, fallbackCurrency);
                    if (product is not null && product.Offers.Count > 0)
                    {
                        return product;
                    }
                }
            }
        }

        return ParseMicrodata(document, productUrl, fallbackCurrency)
            ?? ParseMetaTags(document, productUrl, fallbackCurrency);
    }

    /// <summary>schema.org microdata (itemprop attributes), used by many WooCommerce, OpenCart and older platforms.</summary>
    private static ScrapedProduct? ParseMicrodata(AngleSharp.Html.Dom.IHtmlDocument document, Uri productUrl, string fallbackCurrency)
    {
        var scope = document.QuerySelector("[itemtype*='schema.org/Product']");
        if (scope is null)
        {
            return null;
        }

        string? Prop(AngleSharp.Dom.IElement root, string name)
        {
            var element = root.QuerySelector($"[itemprop='{name}']");
            var value = element?.GetAttribute("content") ?? element?.GetAttribute("href") ?? element?.TextContent;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var title = Prop(scope, "name");
        if (title is null)
        {
            return null;
        }

        var offers = new List<ScrapedOffer>();
        var offerScopes = scope.QuerySelectorAll("[itemprop='offers']").ToList();
        foreach (var offer in offerScopes.Count > 0 ? offerScopes : [scope])
        {
            var price = PriceParser.Parse(Prop(offer, "price") ?? Prop(offer, "lowPrice"));
            if (price is null)
            {
                continue;
            }

            var availability = Prop(offer, "availability");
            bool? inStock = availability is null ? null
                : availability.Contains("InStock", StringComparison.OrdinalIgnoreCase) ? true
                : availability.Contains("OutOfStock", StringComparison.OrdinalIgnoreCase) ? false
                : null;

            offers.Add(new ScrapedOffer(
                VariantKey: Prop(offer, "sku") ?? (offers.Count == 0 ? "" : $"#{offers.Count}"),
                SizeLabel: Prop(offer, "size"),
                Price: price.Value,
                ListPrice: null,
                Currency: (Prop(offer, "priceCurrency") ?? fallbackCurrency).ToUpperInvariant(),
                InStock: inStock));
        }

        if (offers.Count == 0)
        {
            return null;
        }

        var url = StripQuery(productUrl);
        return new ScrapedProduct(
            ExternalId: Prop(scope, "sku") ?? Prop(scope, "productID") ?? url,
            Url: url,
            Title: System.Net.WebUtility.HtmlDecode(title),
            Brand: Prop(scope, "brand"),
            ImageUrl: Prop(scope, "image"),
            Offers: DistinctByVariant(offers));
    }

    private static JsonElement? FindProductNode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                JsonElement? product = null;
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindProductNode(item);
                    if (found is not null && HasType(found.Value, "ProductGroup"))
                    {
                        return found;
                    }

                    product ??= found;
                }

                return product;

            case JsonValueKind.Object:
                if (HasType(element, "ProductGroup") || HasType(element, "Product"))
                {
                    return element;
                }

                return element.TryGetProperty("@graph", out var graph) ? FindProductNode(graph) : null;

            default:
                return null;
        }
    }

    private static ScrapedProduct? MapProduct(JsonElement node, Uri productUrl, string fallbackCurrency)
    {
        var title = Text(node, "name");
        if (title is null)
        {
            return null;
        }

        var offers = new List<ScrapedOffer>();
        if (HasType(node, "ProductGroup") && node.TryGetProperty("hasVariant", out var variants))
        {
            foreach (var variant in AsArray(variants))
            {
                var variantKey = Text(variant, "sku") ?? Text(variant, "productID") ?? Text(variant, "@id");
                var size = SizeText(variant);
                if (variant.TryGetProperty("offers", out var variantOffers))
                {
                    AddOffers(variantOffers, variantKey, size, fallbackCurrency, offers);
                }
            }
        }

        if (offers.Count == 0 && node.TryGetProperty("offers", out var productOffers))
        {
            AddOffers(productOffers, Text(node, "sku"), SizeText(node), fallbackCurrency, offers);
        }

        var externalId = Text(node, "productGroupID") ?? Text(node, "productID") ?? Text(node, "sku")
            ?? Text(node, "mpn") ?? StripQuery(productUrl);

        return new ScrapedProduct(
            ExternalId: externalId,
            Url: StripQuery(productUrl),
            Title: title,
            Brand: node.TryGetProperty("brand", out var brand) ? NameOf(brand) : null,
            ImageUrl: node.TryGetProperty("image", out var image) ? ImageOf(image) : null,
            Offers: DistinctByVariant(offers));
    }

    private static void AddOffers(JsonElement offersElement, string? variantKey, string? size, string fallbackCurrency, List<ScrapedOffer> into)
    {
        foreach (var offer in AsArray(offersElement))
        {
            if (HasType(offer, "AggregateOffer"))
            {
                if (offer.TryGetProperty("offers", out var nested))
                {
                    AddOffers(nested, variantKey, size, fallbackCurrency, into);
                    continue;
                }

                var low = offer.TryGetProperty("lowPrice", out var lowElement) ? PriceParser.Parse(lowElement) : null;
                if (low is not null)
                {
                    into.Add(new ScrapedOffer(variantKey ?? "", size, low.Value, null,
                        Text(offer, "priceCurrency") ?? fallbackCurrency, Availability(offer)));
                }

                continue;
            }

            var (price, listPrice, currency) = ReadPrice(offer);
            if (price is null)
            {
                continue;
            }

            var offerSize = size;
            var offerKey = Text(offer, "sku") ?? variantKey;
            if (offer.TryGetProperty("itemOffered", out var itemOffered) && itemOffered.ValueKind == JsonValueKind.Object)
            {
                offerSize ??= SizeText(itemOffered);
                offerKey ??= Text(itemOffered, "sku");
            }

            into.Add(new ScrapedOffer(
                VariantKey: offerKey ?? (into.Count == 0 ? "" : $"#{into.Count}"),
                SizeLabel: offerSize,
                Price: price.Value,
                ListPrice: listPrice > price ? listPrice : null,
                Currency: (currency ?? fallbackCurrency).ToUpperInvariant(),
                InStock: Availability(offer)));
        }
    }

    private static (decimal? Price, decimal? ListPrice, string? Currency) ReadPrice(JsonElement offer)
    {
        var price = offer.TryGetProperty("price", out var priceElement) ? PriceParser.Parse(priceElement) : null;
        var currency = Text(offer, "priceCurrency");
        decimal? listPrice = null;

        if (offer.TryGetProperty("priceSpecification", out var specs))
        {
            foreach (var spec in AsArray(specs))
            {
                var specPrice = spec.TryGetProperty("price", out var specPriceElement) ? PriceParser.Parse(specPriceElement) : null;
                var priceType = Text(spec, "priceType") ?? "";
                if (priceType.Contains("ListPrice", StringComparison.OrdinalIgnoreCase)
                    || priceType.Contains("StrikethroughPrice", StringComparison.OrdinalIgnoreCase)
                    || priceType.Contains("MSRP", StringComparison.OrdinalIgnoreCase))
                {
                    listPrice = specPrice;
                }
                else
                {
                    price ??= specPrice;
                }

                currency ??= Text(spec, "priceCurrency");
            }
        }

        return (price, listPrice, currency);
    }

    private static ScrapedProduct? ParseMetaTags(AngleSharp.Html.Dom.IHtmlDocument document, Uri productUrl, string fallbackCurrency)
    {
        string? Meta(string property) =>
            document.QuerySelector($"meta[property='{property}']")?.GetAttribute("content")
            ?? document.QuerySelector($"meta[name='{property}']")?.GetAttribute("content");

        var price = PriceParser.Parse(Meta("product:price:amount") ?? Meta("og:price:amount"));
        var title = Meta("og:title") ?? document.Title;
        if (price is null || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var currency = Meta("product:price:currency") ?? Meta("og:price:currency") ?? fallbackCurrency;
        var url = StripQuery(productUrl);
        return new ScrapedProduct(
            ExternalId: url,
            Url: url,
            Title: title.Trim(),
            Brand: Meta("product:brand") ?? Meta("og:brand"),
            ImageUrl: Meta("og:image"),
            Offers: [new ScrapedOffer("", null, price.Value, null, currency.ToUpperInvariant(), null)]);
    }

    private static IReadOnlyList<ScrapedOffer> DistinctByVariant(List<ScrapedOffer> offers) =>
        offers.GroupBy(o => o.VariantKey).Select(g => g.First()).ToList();

    private static bool? Availability(JsonElement offer)
    {
        var availability = Text(offer, "availability");
        if (availability is null)
        {
            return null;
        }

        if (availability.Contains("InStock", StringComparison.OrdinalIgnoreCase)
            || availability.Contains("LimitedAvailability", StringComparison.OrdinalIgnoreCase)
            || availability.Contains("OnlineOnly", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return availability.Contains("OutOfStock", StringComparison.OrdinalIgnoreCase)
            || availability.Contains("SoldOut", StringComparison.OrdinalIgnoreCase)
            || availability.Contains("Discontinued", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
    }

    private static string? SizeText(JsonElement node) =>
        node.TryGetProperty("size", out var size) ? NameOf(size) : null;

    private static bool HasType(JsonElement node, string type)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("@type", out var typeElement))
        {
            return false;
        }

        return AsArray(typeElement).Any(t => t.ValueKind == JsonValueKind.String
            && (t.GetString() == type || t.GetString()!.EndsWith("/" + type, StringComparison.Ordinal)));
    }

    private static IEnumerable<JsonElement> AsArray(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [element];

    private static string? Text(JsonElement node, string property)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(property, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : System.Net.WebUtility.HtmlDecode(text.Trim());
    }

    private static string? NameOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? null : element.GetString()!.Trim(),
        JsonValueKind.Object => Text(element, "name"),
        JsonValueKind.Array => element.EnumerateArray().Select(NameOf).FirstOrDefault(n => n is not null),
        _ => null,
    };

    private static string? ImageOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Object => Text(element, "url") ?? Text(element, "contentUrl"),
        JsonValueKind.Array => element.EnumerateArray().Select(ImageOf).FirstOrDefault(i => i is not null),
        _ => null,
    };

    private static string StripQuery(Uri url) => url.GetLeftPart(UriPartial.Path);
}
