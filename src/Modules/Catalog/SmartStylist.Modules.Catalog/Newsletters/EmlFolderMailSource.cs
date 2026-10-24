using System.Runtime.CompilerServices;
using MimeKit;

namespace SmartStylist.Modules.Catalog.Newsletters;

/// <summary>
/// Reads saved .eml files from a folder. Handy for a first test without mailbox access:
/// in Gmail, open an email, "Download message", and drop the file in the folder.
/// </summary>
public sealed class EmlFolderMailSource(string folder) : IMailSource
{
    public string Description => folder;

    public async IAsyncEnumerable<InboxMessage> ReadAsync(DateTimeOffset since, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Folder '{folder}' does not exist.");
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*.eml").Order(StringComparer.Ordinal))
        {
            // Saved files are imported whatever their date: the user chose them on purpose.
            var message = await MimeMessage.LoadAsync(path, cancellationToken);
            yield return InboxMessage.FromMime(message);
        }
    }
}
