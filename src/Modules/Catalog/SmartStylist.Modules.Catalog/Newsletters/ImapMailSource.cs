using System.Runtime.CompilerServices;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>
/// Reads a mailbox folder over IMAP. Read-only: nothing is marked, moved or deleted,
/// so pointing it at a real inbox is safe.
/// </summary>
public sealed class ImapMailSource(ImapOptions options) : IMailSource
{
    public string Description => $"{options.Host}/{options.Folder}";

    public async IAsyncEnumerable<InboxMessage> ReadAsync(DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException(
                "Newsletters:Imap needs Host, Username and Password. Set the password with: " +
                "dotnet user-secrets set \"Newsletters:Imap:Password\" \"<app password>\" --project src/SmartStylist.Worker");
        }

        using var client = new ImapClient();
        await client.ConnectAsync(options.Host, options.Port, options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);

        var folder = await client.GetFolderAsync(options.Folder, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        // IMAP SINCE has day precision; anything older is filtered again below.
        var ids = await folder.SearchAsync(SearchQuery.DeliveredAfter(since.UtcDateTime.Date.AddDays(-1)), cancellationToken);
        foreach (var id in ids)
        {
            var message = await folder.GetMessageAsync(id, cancellationToken);
            if (message.Date >= since)
            {
                yield return InboxMessage.FromMime(message);
            }
        }

        await client.DisconnectAsync(true, cancellationToken);
    }
}
