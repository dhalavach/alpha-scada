/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Program.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Asset;
using Alpha.Scada.Asset.Application;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;

const string serviceName = "alpha-scada-asset";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<AssetMigrator>();
builder.Services.AddSingleton<AssetRepository>();
builder.Services.AddSingleton<AssetService>();
builder.Services.AddSingleton<TenantKeyResolver>();
builder.Services.AddMemoryCache();
builder.Services.AddHostedService<CommunicationLossMonitorWorker>();
builder.Services.AddAlphaServiceClients(builder.Configuration, AlphaServiceClients.Tenant);
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

internalApi.MapGet("/sites", async (AuthenticatedUser user, AssetService service, HttpContext context) =>
    Results.Ok(await service.GetSitesAsync(user.Current, context.RequestAborted)));

internalApi.MapGet("/sites/{siteId:guid}/units", async (Guid siteId, AuthenticatedUser user, AssetService service, HttpContext context) =>
    Results.Ok(await service.GetUnitsForSiteAsync(siteId, user.Current, context.RequestAborted)));

internalApi.MapGet("/units/{unitId:guid}", async (Guid unitId, AuthenticatedUser user, AssetService service, HttpContext context) =>
{
    var unit = await service.GetUnitAsync(unitId, user.Current, context.RequestAborted);
    return unit is null ? Results.NotFound() : Results.Ok(unit);
});

app.MapGet("/internal/v1/units/resolve", async (Guid tenantId, string siteKey, string unitKey, AssetService service, CancellationToken cancellationToken) =>
{
    var unit = await service.ResolveUnitAsync(tenantId, siteKey, unitKey, cancellationToken);
    return unit is null ? Results.NotFound() : Results.Ok(unit);
});

app.MapGet("/internal/v1/units/{unitId:guid}/route", async (Guid unitId, AssetService service, CancellationToken cancellationToken) =>
{
    var unit = await service.GetUnitRouteAsync(unitId, cancellationToken);
    return unit is null ? Results.NotFound() : Results.Ok(unit);
});

app.MapGet("/internal/v1/units/stale", async (int? minutes, AssetService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetStaleUnitsAsync(minutes ?? 2, cancellationToken)));

app.Run();
