using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Asset.Application;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine.MQTT;

const string serviceName = "alpha-scada-asset";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<AssetMigrator>();
builder.Services.AddSingleton<AssetRepository>();
builder.Services.AddSingleton<AssetService>();
builder.Services.AddSingleton<TenantKeyResolver>();
builder.Services.AddMemoryCache();
builder.Services.AddHostedService<CommunicationLossMonitorWorker>();
builder.Services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Tenant"] ?? "http://localhost:5211"));
builder.Services.AddJwtTokenService(builder.Configuration);
builder.Host.UseAlphaMessaging("asset", options =>
{
    options.PublishMessagesToMqttTopic<UnitStatusChanged>(message => Topics.Status(message.TenantKey, message.SiteKey, message.UnitKey));
    options.ListenToMqttTopic(Topics.TelemetryStoredWildcard);
});

var app = builder.Build();
await app.Services.GetRequiredService<AssetMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", MinimalApi.ReadyAsync);
app.MapGet("/metrics", () => MinimalApi.Metrics(serviceName));

app.MapGet("/internal/v1/sites", async (HttpContext context, JwtTokenService tokens, AssetService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(await service.GetSitesAsync(user, context.RequestAborted));
});

app.MapGet("/internal/v1/sites/{siteId:guid}/units", async (Guid siteId, HttpContext context, JwtTokenService tokens, AssetService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(await service.GetUnitsForSiteAsync(siteId, user, context.RequestAborted));
});

app.MapGet("/internal/v1/units/{unitId:guid}", async (Guid unitId, HttpContext context, JwtTokenService tokens, AssetService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    if (user is null) return Results.Unauthorized();
    var unit = await service.GetUnitAsync(unitId, user, context.RequestAborted);
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

app.MapPost("/internal/v1/units/{unitId:guid}/online", async (Guid unitId, AssetService service, CancellationToken cancellationToken) =>
{
    await service.SetUnitOnlineAsync(unitId, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/internal/v1/units/offline-stale", async (int? minutes, AssetService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.MarkStaleUnitsOfflineAsync(minutes ?? 2, cancellationToken)));

app.MapGet("/internal/v1/units/stale", async (int? minutes, AssetService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetStaleUnitsAsync(minutes ?? 2, cancellationToken)));

app.Run();
