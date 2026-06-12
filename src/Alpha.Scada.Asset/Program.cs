using Alpha.Scada.Asset;
using Alpha.Scada.Asset.Application;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;

const string serviceName = "alpha-scada-asset";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
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
app.UseAlphaExceptionHandling();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

internalApi.MapGet("/sites", async (AuthenticatedUser user, AssetService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetSitesAsync(user.Current, cancellationToken)));

internalApi.MapGet("/sites/{siteId:guid}/units", async (Guid siteId, AuthenticatedUser user, AssetService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetUnitsForSiteAsync(siteId, user.Current, cancellationToken)));

internalApi.MapGet("/units/{unitId:guid}", async (Guid unitId, AuthenticatedUser user, AssetService service, CancellationToken cancellationToken) =>
{
    var unit = await service.GetUnitAsync(unitId, user.Current, cancellationToken);
    return unit is null ? Results.NotFound() : Results.Ok(unit);
});

internalApi.MapGet("/units/resolve", async (Guid tenantId, string siteKey, string unitKey, AssetService service, CancellationToken cancellationToken) =>
{
    var unit = await service.ResolveUnitAsync(tenantId, siteKey, unitKey, cancellationToken);
    return unit is null ? Results.NotFound() : Results.Ok(unit);
}).RequireAuthorization(AlphaAuthentication.ServiceOnlyPolicy);

internalApi.MapGet("/units/{unitId:guid}/route", async (Guid unitId, AssetService service, CancellationToken cancellationToken) =>
{
    var unit = await service.GetUnitRouteAsync(unitId, cancellationToken);
    return unit is null ? Results.NotFound() : Results.Ok(unit);
}).RequireAuthorization(AlphaAuthentication.ServiceOnlyPolicy);

app.Run();
