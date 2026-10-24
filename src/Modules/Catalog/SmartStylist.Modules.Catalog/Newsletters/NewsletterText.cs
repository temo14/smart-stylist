using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>
/// An email turned into plain text for the model. Links become short ids like [L3] so the model
/// never has to copy long tracking URLs, and can only point at links that are really in the email.
/// </summary>
internal sealed partial class NewsletterText
{
    private NewsletterText(string text, IReadOnlyList<string> links)
    {
        Text = text;
        Links = links;
    }

    public string Text { get; }

    /// <summary>Link URLs; [L1] is index 0.</summary>
    public IReadOnlyList<string> Links { get; }

    public string? LinkUrl(string? linkId)
    {
        if (linkId is null)
        {
            return null;
        }

        var match = LinkIdRegex().Match(linkId);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) && n >= 1 && n <= Links.Count
            ? Links[n - 1]
            : null;
    }

    public static NewsletterText From(InboxMessage message, int maxChars)
    {
        var builder = new StringBuilder();
        var links = new List<string>();

        if (!string.IsNullOrWhiteSpace(message.Html))
        {
            var document = new HtmlParser().ParseDocument(message.Html);
            foreach (var junk in document.QuerySelectorAll("script, style, head, noscript").ToList())
            {
                junk.Remove();
            }

            if (document.Body is { } body)
            {
                Walk(body, builder, links);
            }
        }
        else if (!string.IsNullOrWhiteSpace(message.Text))
        {
            builder.Append(BareUrlRegex().Replace(message.Text, m => LinkToken(m.Value, links)));
        }

        var text = Tidy(builder.ToString());
        if (text.Length > maxChars)
        {
            text = text[..maxChars];
        }

        return new NewsletterText(text, links);
    }

    private static void Walk(INode node, StringBuilder builder, List<string> links)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    builder.Append(text.Data);
                    break;
                case IElement { LocalName: "br" }:
                    builder.Append('\n');
                    break;
                case IElement { LocalName: "img" } img:
                    var alt = img.GetAttribute("alt");
                    if (!string.IsNullOrWhiteSpace(alt))
                    {
                        builder.Append(" [image: ").Append(alt.Trim()).Append("] ");
                    }

                    break;
                case IElement { LocalName: "a" } anchor:
                    Walk(anchor, builder, links);
                    var href = anchor.GetAttribute("href")?.Trim();
                    if (href is not null && href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.Append(' ').Append(LinkToken(href, links)).Append(' ');
                    }

                    break;
                case IElement element:
                    var block = IsBlock(element.LocalName);
                    if (block)
                    {
                        builder.Append('\n');
                    }
                    else if (element.LocalName is "td" or "th")
                    {
                        builder.Append(" | ");
                    }

                    Walk(element, builder, links);
                    if (block)
                    {
                        builder.Append('\n');
                    }

                    break;
            }
        }
    }

    private static string LinkToken(string url, List<string> links)
    {
        var index = links.IndexOf(url);
        if (index < 0)
        {
            links.Add(url);
            index = links.Count - 1;
        }

        return $"[L{index + 1}]";
    }

    private static bool IsBlock(string name) =>
        name is "p" or "div" or "tr" or "table" or "li" or "ul" or "ol" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "section" or "center";

    private static string Tidy(string text)
    {
        text = text.Replace(' ', ' ').Replace("‌", "").Replace("​", "").Replace("͏", "");
        text = SpacesRegex().Replace(text, " ");
        text = EmptyCellsRegex().Replace(text, " ");
        text = BlankLinesRegex().Replace(text, "\n");
        return text.Trim();
    }

    [GeneratedRegex(@"^\s*\[?L(\d+)\]?\s*$")]
    private static partial Regex LinkIdRegex();

    [GeneratedRegex(@"https?://[^\s<>""')\]]+")]
    private static partial Regex BareUrlRegex();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"(\s*\|\s*)+(?=\||\n|$)")]
    private static partial Regex EmptyCellsRegex();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex BlankLinesRegex();
}
