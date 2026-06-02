using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine.MQTT;

const string serviceName = "alpha-scada-alarm";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<AlarmMigrator>();
builder.Services.AddSingleton<AlarmRepository>();
builder.Services.AddSingleton<AlarmService>();
builder.Services.AddSingleton<UnitKeyResolver>();
builder.Services.AddSingleton<ThresholdCache>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("asset", client => client.BaseAddress = new Uri(builder.Configuration["Services:Asset"] ?? "http://localhost:5212")).AddAlphaResilience();
builder.Services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(builder.Configuration["Services:Tenant"] ?? "http://localhost:5211")).AddAlphaResilience();
builder.Services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(builder.Configuration["Services:TagCatalog"] ?? "http://localhost:5213")).AddAlphaResilience();
builder.Services.AddJwtTokenService(builder.Configuration);
builder.Host.UseAlphaMessaging("alarm", options =>
{
    options.PublishMessagesToMqttTopic<AlarmRaised>(message => Topics.AlarmRaised(message.TenantKey, message.SiteKey, message.UnitKey));
    options.PublishMessagesToMqttTopic<AlarmCleared>(message => Topics.AlarmCleared(message.TenantKey, message.SiteKey, message.UnitKey));
    options.PublishMessagesToMqttTopic<AlarmAcknowledged>(message => Topics.AlarmAcknowledged(message.TenantKey, message.SiteKey, message.UnitKey));
    options.ListenToMqttTopic(Topics.StatusWildcard);
    options.ListenToMqttTopic(Topics.TelemetryStoredWildcard);
});

var app = builder.Build();
await app.Services.GetRequiredService<AlarmMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.ReadyAsync(dataSource, cancellationToken));
app.MapGet("/metrics", (Npgsql.NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

app.MapGet("/internal/v1/alarms/active", async (HttpContext context, JwtTokenService tokens, AlarmService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    return user is null ? Results.Unauthorized() : Results.Ok(await service.GetActiveAsync(user, context.RequestAborted));
});

app.MapPost("/internal/v1/alarms/{alarmId:guid}/ack", async (Guid alarmId, HttpContext context, JwtTokenService tokens, AlarmService service) =>
{
    var user = HttpUserContext.FromBearerToken(context.Request.Headers, tokens);
    if (user is null) return Results.Unauthorized();
    if (!RoleRules.CanAcknowledge(user.Role)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var changed = await service.AcknowledgeAsync(alarmId, user, context.RequestAborted);
    return changed ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/internal/v1/alarms/count", async (Guid unitId, string period, AlarmService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CountForUnitPeriodAsync(unitId, period, cancellationToken)));

app.Run();
