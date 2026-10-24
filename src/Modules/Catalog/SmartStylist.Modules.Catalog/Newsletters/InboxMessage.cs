using System.Security.Cryptography;
using System.Text;
using MimeKit;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>An email as read from the mailbox, before any extraction.</summary>
public sealed record InboxMessage(
    string MessageId,
    string FromAddress,
    string Subject,
    DateTimeOffset ReceivedAt,
    string? Html,
    string? Text)
{
    public static InboxMessage FromMime(MimeMessage message)
    {
        var from = message.From.Mailboxes.FirstOrDefault()?.Address ?? "";
        var subject = message.Subject ?? "";
        var id = string.IsNullOrWhiteSpace(message.MessageId)
            ? "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{from}|{message.Date:O}|{subject}")))
            : message.MessageId;
        return new InboxMessage(id, from, subject, message.Date, message.HtmlBody, message.TextBody);
    }
}
