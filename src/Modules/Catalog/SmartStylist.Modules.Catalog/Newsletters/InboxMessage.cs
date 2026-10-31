using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using MimeKit;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>An email as read from the mailbox, before any extraction.</summary>
/// <param name="FromAddress">The shop's address. For a forwarded email this is the original sender, not the person who forwarded it.</param>
/// <param name="ForwardedBy">Who forwarded the email, when it was forwarded.</param>
public sealed partial record InboxMessage(
    string MessageId,
    string FromAddress,
    string Subject,
    DateTimeOffset ReceivedAt,
    string? Html,
    string? Text,
    string? ForwardedBy = null)
{
    public static InboxMessage FromMime(MimeMessage message)
    {
        var from = message.From.Mailboxes.FirstOrDefault()?.Address ?? "";

        // "Forward as attachment" (Outlook, Gmail): the shop's email travels whole inside this one.
        if (message.BodyParts.OfType<MessagePart>().FirstOrDefault()?.Message is { } attached)
        {
            return FromMime(attached) with { ForwardedBy = from };
        }

        var subject = message.Subject ?? "";
        var originalSender = InlineForwardSender(message);
        if (originalSender is not null)
        {
            subject = ForwardPrefixRegex().Replace(subject, "");
        }

        var id = string.IsNullOrWhiteSpace(message.MessageId)
            ? "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{from}|{message.Date:O}|{subject}")))
            : message.MessageId;

        // Emails carry the sender's offset; Postgres only stores UTC.
        return new InboxMessage(id, originalSender ?? from, subject, message.Date.ToUniversalTime(), message.HtmlBody, message.TextBody,
            ForwardedBy: originalSender is null ? null : from);
    }

    /// <summary>
    /// The original sender from an inline forward, e.g. Gmail's "---------- Forwarded message ---------" block
    /// followed by "From: J.Crew &lt;jcrew@e.jcrew.com&gt;".
    /// </summary>
    private static string? InlineForwardSender(MimeMessage message)
    {
        var text = message.TextBody;
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            text = new HtmlParser().ParseDocument(message.HtmlBody).Body?.TextContent;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = ForwardedFromRegex().Match(text.Length > 20_000 ? text[..20_000] : text);
        return match.Success ? match.Groups["address"].Value : null;
    }

    [GeneratedRegex(@"(?:Forwarded message|Begin forwarded message|Original Message|_{10,})[\s\S]{0,300}?From:[^\n<@]*<?(?<address>[\w.+-]+@[\w-]+(?:\.[\w-]+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex ForwardedFromRegex();

    [GeneratedRegex(@"^\s*((fwd?|fw)\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex ForwardPrefixRegex();
}
