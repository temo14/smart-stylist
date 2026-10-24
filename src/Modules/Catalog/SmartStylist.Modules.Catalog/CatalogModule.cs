using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Modules.Catalog.Persistence;
using SmartStylist.Modules.Catalog.Reporting;

namespace SmartStylist.Modules.Catalog;

public static class CatalogModule
{
    public const string ConnectionStringName = "Postgres";

    /// <summary>Registers the Catalog module: products, daily prices, collectors and the data-quality report.</summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogOptions>()
            .Bind(configuration.GetSection(CatalogOptions.SectionName))
            .Validate(o => o.Shops.Select(s => s.Key).Distinct().Count() == o.Shops.Count, "Shop keys must be unique.")
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Missing connection string '{ConnectionStringName}'.");

        services.AddDbContext<CatalogDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddCatalogCollection();
        return services;
    }

    /// <summary>Collector services without the database registration, so tests can plug in their own provider.</summary>
    internal static IServiceCollection AddCatalogCollection(this IServiceCollection services)
    {
        services.AddHttpClient<HttpPageFetcher>(HttpPageFetcher.ClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<CatalogOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,*/*;q=0.7");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<BrowserPageFetcher>();
        services.AddSingleton<SitemapReader>();
        services.AddScoped<IShopCollector, ShopifyCollector>();
        services.AddScoped<IShopCollector, SitemapJsonLdCollector>();
        services.AddScoped<IShopCollector, BrowserJsonLdCollector>();
        services.AddScoped(sp => new PriceCollectionService(
            sp.GetRequiredService<CatalogDbContext>(),
            sp.GetServices<IShopCollector>(),
            sp.GetRequiredService<IOptions<CatalogOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PriceCollectionService>>()));
        services.AddScoped<ShopCatalogSync>();
        services.AddScoped(sp => new ProductPageProbe(sp.GetRequiredService<HttpPageFetcher>(), sp.GetRequiredService<BrowserPageFetcher>()));
        services.AddScoped<CollectPricesJob>();
        services.AddScoped<DataQualityReport>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
