using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>
/// Reads product prices out of a sale email with Claude. The model only reads; every price it
/// returns is checked against the email text by <see cref="ExtractionGuard"/> before it is stored.
/// </summary>
internal sealed class ClaudeNewsletterExtractor(IOptions<NewsletterOptions> options) : INewsletterExtractor
{
    private const string Instructions = """
        You read promotional emails from clothing and shoe shops and list the products whose prices the email shows.

        Rules:
        - Only list a product when its price is printed in the email text. Never guess or calculate a price.
        - Copy "price" and "list_price" exactly as printed, currency symbol included. "list_price" is the crossed-out
          or "was" price; use null when the email shows only one price.
        - When a percentage off is announced without a printed price for a product, do not list that product.
        - "link_id" is the id in square brackets (like L12) right after the product's name, image or "shop now" text.
          Use null when no link clearly belongs to the product.
        - Image descriptions appear as [image: ...]; prices inside them count only if they look like real price text.
        - Set has_product_prices to false and return an empty list when the email has no product prices.
        - sale_ends_on: the last day of the sale as YYYY-MM-DD, only when the email states an end date. Use the
          email's received date to resolve dates like "ends Sunday".
        """;

    private AnthropicClient? _client;

    public async Task<NewsletterExtraction> ExtractAsync(NewsletterInput input, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var response = await Client(settings).Messages.Create(new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = 16000,
            System = Instructions,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = NewsletterExtraction.Schema },
            },
            Messages =
            [
                new UserMessageParam($"""
                        Shop: {input.ShopName}
                        Received: {input.ReceivedAt:yyyy-MM-dd (dddd)}
                        Subject: {input.Subject}

                        <email>
                        {input.Text}
                        </email>
                        """),
            ],
        }, cancellationToken);

        if (response.StopReason == "max_tokens")
        {
            throw new InvalidOperationException("The email listed more products than fit in one answer.");
        }

        if (response.StopReason == "refusal")
        {
            throw new InvalidOperationException("The model declined to read this email.");
        }

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return NewsletterExtraction.Parse(json);
    }

    private AnthropicClient Client(NewsletterOptions settings)
    {
        if (_client is not null)
        {
            return _client;
        }

        var apiKey = string.IsNullOrWhiteSpace(settings.AnthropicApiKey)
            ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            : settings.AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "No Anthropic API key. Run: dotnet user-secrets set \"Newsletters:AnthropicApiKey\" \"<key>\" --project src/SmartStylist.Worker");
        }

        return _client = new AnthropicClient { ApiKey = apiKey };
    }
}
