/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Program.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Infrastructure;

const string serviceName = "alpha-scada-telemetry";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<TelemetryMigrator>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<TelemetryService>();
builder.Services.AddSingleton<CatalogCache>();
builder.Services.AddSingleton<ITelemetryAdapter, NatsJsonTelemetryAdapter>();
builder.Services.AddSingleton<TelemetryAdapterResolver>();
builder.Services.AddSingleton<CanonicalTelemetryHandler>();
builder.Services.AddHostedService<TelemetryAdapterIngestionWorker>();
builder.Services.AddMemoryCache();
builder.Services.AddAlphaServiceClients(
    builder.Configuration,
    AlphaServiceClients.Tenant,
    AlphaServiceClients.Asset,
    AlphaServiceClients.TagCatalog);
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
