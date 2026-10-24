using SmartStylist.Modules.Catalog.Newsletters;

namespace SmartStylist.Modules.Catalog.Tests;

public class NewsletterTextTests
{
    private static async Task<InboxMessage> LoadFixtureAsync()
    {
        var source = new EmlFolderMailSource(Path.Combine(AppContext.BaseDirectory, "Fixtures", "emails"));
        var messages = new List<InboxMessage>();
        await foreach (var message in source.ReadAsync(DateTimeOffset.MinValue, CancellationToken.None))
        {
            messages.Add(message);
        }

        return Assert.Single(messages);
    }

    [Fact]
    public async Task Eml_files_are_read_with_headers()
    {
        var message = await LoadFixtureAsync();

        Assert.Equal("<sale-2026-10-23@e.jcrew.com>".Trim('<', '>'), message.MessageId.Trim('<', '>'));
        Assert.Equal("jcrew@e.jcrew.com", message.FromAddress);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 14, 5, 0, TimeSpan.Zero), message.ReceivedAt);
        Assert.NotNull(message.Html);
    }

    [Fact]
    public async Task Html_becomes_text_with_numbered_links_and_image_alts()
    {
        var text = NewsletterText.From(await LoadFixtureAsync(), maxChars: 10_000);

        Assert.Contains("[image: Slim-fit chino]", text.Text);
        Assert.Contains("$89.50 $53.70", text.Text);
        Assert.Contains("Use code SHOPNOW", text.Text);
        Assert.DoesNotContain("color:red", text.Text);
        Assert.DoesNotContain("click.e.jcrew.com", text.Text);
        Assert.Equal(4, text.Links.Count);
        Assert.Equal("https://click.e.jcrew.com/?qs=chino", text.LinkUrl("L2"));
        Assert.Equal("https://click.e.jcrew.com/?qs=chino", text.LinkUrl("[L2]"));
        Assert.Null(text.LinkUrl("L9"));
        Assert.Null(text.LinkUrl(null));
    }

    [Fact]
    public void Plain_text_emails_get_their_bare_urls_numbered()
    {
        var message = new InboxMessage("id", "a@b.com", "s", DateTimeOffset.UnixEpoch, Html: null,
            Text: "Chino $50 https://shop.example.com/p/chino?utm=1\nSame again https://shop.example.com/p/chino?utm=1");

        var text = NewsletterText.From(message, 10_000);

        Assert.Equal("Chino $50 [L1]\nSame again [L1]", text.Text);
        Assert.Single(text.Links);
    }

    [Fact]
    public void Long_emails_are_cut()
    {
        var message = new InboxMessage("id", "a@b.com", "s", DateTimeOffset.UnixEpoch, Html: null, Text: new string('x', 500));

        Assert.Equal(100, NewsletterText.From(message, 100).Text.Length);
    }
}
