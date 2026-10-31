using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using SmartStylist.Modules.Catalog.Newsletters;
using SmartStylist.Modules.Catalog.Persistence;
using Testcontainers.PostgreSql;

namespace SmartStylist.Modules.Catalog.Tests;

/// <summary>Runs against real Postgres (needs Docker): SQLite accepts the local offsets that Postgres refuses.</summary>
public class EmailDatesOnPostgresTests : IClassFixture<EmailDatesOnPostgresTests.Database>
{
    private static readonly ShopOptions JCrew = new() { Key = "jcrew", Name = "J.Crew", BaseUrl = "https://www.jcrew.com", Currency = "USD", Enabled = false };

    private readonly Database _database;

    public EmailDatesOnPostgresTests(Database database) => _database = database;

    [Theory]
    [InlineData("Sat, 24 Oct 2026 10:05:00 +0400", "2026-10-24T06:05:00Z", "2026-10-24")]
    [InlineData("Sat, 24 Oct 2026 22:30:00 -0400", "2026-10-25T02:30:00Z", "2026-10-25")]
    [InlineData("Fri, 23 Oct 2026 14:05:00 +0000", "2026-10-23T14:05:00Z", "2026-10-23")]
    public async Task Email_times_are_stored_in_utc_and_priced_on_the_utc_day(string dateHeader, string storedAt, string observedOn)
    {
        var mime = new MimeMessage { Subject = "Extra 40% off", MessageId = $"{storedAt}@e.jcrew.com", Body = new TextPart("html") { Text = "<a href=\"https://click.e.jcrew.com/chino\">Chino</a> $53.70" } };
        mime.From.Add(MailboxAddress.Parse("jcrew@e.jcrew.com"));
        mime.Headers[HeaderId.Date] = dateHeader;

        await using var host = TestHost.OnPostgres(_database.ConnectionString, JCrew);
        await host.InitializeAsync();
        host.Extractor.Respond = _ => new NewsletterExtraction(true, null, null, [new ExtractedProduct("Chino", "L1", "$53.70", null, "USD")]);

        await host.InScopeAsync(sp => sp.GetRequiredService<NewsletterImportService>()
            .ImportAsync(new ListMailSource(InboxMessage.FromMime(mime)), CancellationToken.None));

        var (email, snapshots) = await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CatalogDbContext>();
            return (await db.NewsletterEmails.SingleAsync(e => e.MessageId == mime.MessageId), await db.PriceSnapshots.ToListAsync());
        });

        Assert.Equal(DateTimeOffset.Parse(storedAt), email.ReceivedAt);
        Assert.Equal(TimeSpan.Zero, email.ReceivedAt.Offset);
        Assert.Contains(snapshots, s => s.ObservedOn == DateOnly.Parse(observedOn) && s.Price == 53.70m);
    }

    public sealed class Database : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

        public string ConnectionString => _container.GetConnectionString();

        public Task InitializeAsync() => _container.StartAsync();

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();
    }
}
