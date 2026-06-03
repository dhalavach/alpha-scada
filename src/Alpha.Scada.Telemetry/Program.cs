using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Infrastructure;

const string serviceName = "alpha-scada-telemetry";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<TelemetryMigrator>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<TelemetryService>();
builder.Services.AddSingleton<CatalogCache>();
builder.Services.AddMemoryCache();
builder.Services.AddHostedService<TelemetryEdgeIngestionWorker>();
builder.Services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Tenant"] ?? "http://localhost:5211")).AddAlphaResilience();
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212")).AddAlphaResilience();
builder.Services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(builder.Configuration["Services:TagCatalog"] ?? "http://localhost:5213")).AddAlphaResilience();
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

internalApi.MapGet("/telemetry/units/{unitId:guid}/current", async (Guid unitId, AuthenticatedUser user, TelemetryService service, HttpContext context) =>
    Results.Ok(await service.GetCurrentAsync(unitId, user.Current, context.RequestAborted)));

internalApi.MapGet("/telemetry/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, AuthenticatedUser user, TelemetryService service, HttpContext context) =>
{
    var window = TimeSpan.FromMinutes(Math.Clamp(minutes ?? 30, 1, 24 * 60));
    return Results.Ok(await service.GetHistoryAsync(tagId, window, user.Current, context.RequestAborted));
});

app.MapPost("/internal/v1/telemetry/units/{unitId:guid}/report-aggregate", async (Guid unitId, ReportAggregateRequest request, TelemetryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetReportAggregateAsync(unitId, request, cancellationToken)));

app.Run();
