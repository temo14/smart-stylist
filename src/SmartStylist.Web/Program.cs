using Hangfire;
using Hangfire.PostgreSql;
using SmartStylist.Modules.Catalog;
using SmartStylist.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCatalogModule(builder.Configuration);

// The web host only reads Hangfire state for the dashboard; jobs run in SmartStylist.Worker.
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        storage => storage.UseNpgsqlConnection(builder.Configuration.GetConnectionString(CatalogModule.ConnectionStringName)),
        new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

// Default dashboard authorization allows local requests only.
app.MapHangfireDashboard("/hangfire");

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
