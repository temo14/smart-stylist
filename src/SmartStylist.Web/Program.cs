using Hangfire;
using Hangfire.PostgreSql;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using SmartStylist.Modules.Catalog;
using SmartStylist.Modules.Catalog.Collection;
using SmartStylist.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCatalogModule(builder.Configuration);
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

// Called by the browser extension. No CORS policy on purpose: only the extension's host permission lets a page reach this.
app.MapPost("/api/captures", (PageCapture capture, PageCaptureService service, CancellationToken cancellationToken) =>
    service.CaptureAsync(capture, cancellationToken));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
