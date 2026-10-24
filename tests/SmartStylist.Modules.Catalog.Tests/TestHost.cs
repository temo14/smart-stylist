using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Modules.Catalog.Newsletters;
using SmartStylist.Modules.Catalog.Persistence;

namespace SmartStylist.Modules.Catalog.Tests;

/// <summary>The Catalog module wired to in-memory SQLite and a fake web, so collection runs end to end offline.</summary>
internal sealed class TestHost : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;

    public FakeWeb Web { get; } = new();

    public FakeExtractor Extractor { get; } = new();

    public FakeLinks Links { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 5, 0, 0, TimeSpan.Zero));

    public TestHost(params ShopOptions[] shops)
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<CatalogOptions>().Configure(o =>
        {
            o.RequestDelayMs = 0;
            o.MaxProductsPerShop = 50;
            o.Shops = [.. shops];
        });
        services.AddDbContext<CatalogDbContext>(o => o
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>());
        services.AddCatalogCollection();
        services.AddHttpClient(HttpPageFetcher.ClientName).ConfigurePrimaryHttpMessageHandler(() => Web);
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<INewsletterExtractor>(Extractor);
        services.AddSingleton<ILinkResolver>(Links);

        // No headless browser in unit tests.
        var browser = services.Single(d => d.ImplementationType == typeof(BrowserJsonLdCollector));
        services.Remove(browser);

        _services = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<ShopCatalogSync>().SyncAsync(CancellationToken.None);
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

internal sealed class FakeExtractor : INewsletterExtractor
{
    public List<NewsletterInput> Inputs { get; } = [];

    public Func<NewsletterInput, NewsletterExtraction> Respond { get; set; } = _ => NewsletterExtraction.Empty;

    public Task<NewsletterExtraction> ExtractAsync(NewsletterInput input, CancellationToken cancellationToken)
    {
        Inputs.Add(input);
        return Task.FromResult(Respond(input));
    }
}

internal sealed class FakeLinks : ILinkResolver
{
    public Dictionary<string, Uri> Targets { get; } = [];

    public Task<Uri?> ResolveAsync(string link, ShopDomains shop, CancellationToken cancellationToken) =>
        Task.FromResult(Targets.GetValueOrDefault(link));
}

internal sealed class ListMailSource(params InboxMessage[] messages) : IMailSource
{
    public string Description => "test";

    public async IAsyncEnumerable<InboxMessage> ReadAsync(DateTimeOffset since, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await Task.Yield();
            yield return message;
        }
    }
}

internal sealed class FakeWeb : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requested { get; } = [];

    public void Serve(string url, string content, string mediaType = "text/html") =>
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType) };

    public void Remove(string url) => _routes.Remove(url);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requested.Add(url);
        return Task.FromResult(_routes.TryGetValue(url, out var respond) ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>SQLite cannot sort DateTimeOffset columns; store them as ticks in tests. Postgres needs none of this.</summary>
internal sealed class SqliteDateTimeOffsetCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}
