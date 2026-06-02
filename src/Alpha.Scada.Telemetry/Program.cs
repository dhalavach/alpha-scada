using Alpha.Scada.Contracts;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using Wolverine.MQTT;

const string serviceName = "alpha-scada-telemetry";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<TelemetryMigrator>();
builder.Services.AddSingleton<TelemetryRepository>();
builder.Services.AddSingleton<TelemetryService>();
builder.Services.AddSingleton<CatalogCache>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Tenant"] ?? "http://localhost:5211")).AddAlphaResilience();
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212")).AddAlphaResilience();
builder.Services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(builder.Configuration["Services:TagCatalog"] ?? "http://localhost:5213")).AddAlphaResilience();
builder.Services.AddJwtTokenService(builder.Configuration);
builder.Host.UseAlphaMessaging("telemetry", options =>
{
    options.ListenToMqttTopic(Topics.TelemetryWildcard)
        .UseInterop(new RawTelemetryEnvelopeMapper())
        .DefaultIncomingMessage(typeof(TelemetryEnvelopeV1));
    options.PublishMessagesToMqttTopic<TelemetryBatchStored>(message =>
        Topics.TelemetryStored(message.TenantKey, message.SiteKey, message.UnitKey));
});

var app = builder.Build();
await app.Services.GetRequiredService<TelemetryMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.ReadyAsync(dataSource, cancellationToken));
app.MapGet("/metrics", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

app.MapGet("/internal/v1/telemetry/units/{unitId:guid}/current", async (Guid unitId, HttpContext context, JwtTokenService tokens, TelemetryService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(await service.GetCurrentAsync(unitId, user, context.RequestAborted));
});

app.MapGet("/internal/v1/telemetry/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, HttpContext context, JwtTokenService tokens, TelemetryService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    if (user is null) return Results.Unauthorized();
    var window = TimeSpan.FromMinutes(Math.Clamp(minutes ?? 30, 1, 24 * 60));
    return Results.Ok(await service.GetHistoryAsync(tagId, window, user, context.RequestAborted));
});

app.MapGet("/internal/v1/telemetry/units/{unitId:guid}/report-aggregate", async (Guid unitId, string period, TelemetryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetReportAggregateAsync(unitId, period, cancellationToken)));

app.Run();
