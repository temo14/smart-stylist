using Hangfire;
using Hangfire.PostgreSql;
using SmartStylist.Modules.Catalog;
using SmartStylist.Worker;

// Usage:
//   dotnet run                         -> Hangfire server; collects every shop daily on Catalog:DailyCron
//   dotnet run -- collect [shop ...] [--limit N]   -> collect now (all enabled shops if none given), then print the report
//   dotnet run -- report               -> print the data-quality report
//   dotnet run -- migrate              -> apply database migrations and sync shops from config
//   dotnet run -- install-browser      -> download headless Chromium for shops that need a real browser
var command = CliCommand.Parse(args);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = command is null ? args : [],
});

builder.Services.AddCatalogModule(builder.Configuration);

if (command is null)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            storage => storage.UseNpgsqlConnection(builder.Configuration.GetConnectionString(CatalogModule.ConnectionStringName)),
            new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));

    builder.Services.AddHangfireServer(options => options.WorkerCount = builder.Configuration.GetValue("Hangfire:WorkerCount", 2));

    // Migrates, syncs shops from config and (re)creates one recurring job per shop on every start.
    builder.Services.AddHostedService<RecurringJobsSetup>();
}

using var host = builder.Build();

if (command is null)
{
    await host.RunAsync();
    return 0;
}

return await command.RunAsync(host.Services, host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
