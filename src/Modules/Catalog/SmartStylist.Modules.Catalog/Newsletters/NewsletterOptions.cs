namespace SmartStylist.Modules.Catalog.Newsletters;

public sealed class NewsletterOptions
{
    public const string SectionName = "Newsletters";

    /// <summary>Cron (UTC) for reading the mailbox. Only scheduled when <see cref="ImapOptions.Host"/> is set.</summary>
    public string Cron { get; set; } = "20 * * * *";

    /// <summary>How far back to look on each read. Emails already read are skipped by Message-ID.</summary>
    public int LookbackDays { get; set; } = 14;

    /// <summary>Claude model that reads the emails.</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>
    /// Anthropic API key. Leave empty in appsettings.json and set it with user-secrets or the
    /// ANTHROPIC_API_KEY environment variable.
    /// </summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>Longest email text sent to the model. Sale emails are usually far below this.</summary>
    public int MaxEmailChars { get; set; } = 60000;

    public ImapOptions Imap { get; set; } = new();
}

public sealed class ImapOptions
{
    /// <summary>For Gmail: imap.gmail.com. Empty turns mailbox reading off.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    public string? Username { get; set; }

    /// <summary>An app password, never the account password. Keep it in user-secrets, not appsettings.json.</summary>
    public string? Password { get; set; }

    /// <summary>Folder or Gmail label holding the sale emails, e.g. "Sales".</summary>
    public string Folder { get; set; } = "INBOX";
}
