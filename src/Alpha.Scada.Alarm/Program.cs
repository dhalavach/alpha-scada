using Alpha.Scada.Alarm;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;

const string serviceName = "alpha-scada-alarm";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddAlphaMigrator<AlarmMigrator>();
builder.Services.AddSingleton<AlarmRepository>();
builder.Services.AddSingleton<AlarmService>();
builder.Services.AddSingleton<AlarmOutboxDispatcher>();
builder.Services.AddSingleton<IAlarmOutboxSignal>(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
builder.Services.AddSingleton<UnitKeyResolver>();
builder.Services.AddSingleton<ThresholdCache>();
builder.Services.AddMemoryCache();
builder.Services.AddAlphaServiceClients(
    builder.Configuration,
    AlphaServiceClients.Asset,
    AlphaServiceClients.Tenant,
    AlphaServiceClients.TagCatalog);
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

internalApi.MapGet("/alarms/active", async (AuthenticatedUser user, AlarmService service, HttpContext context) =>
    Results.Ok(await service.GetActiveAsync(user.Current, context.RequestAborted)));

internalApi.MapPost("/alarms/{alarmId:guid}/ack", async (Guid alarmId, AuthenticatedUser user, AlarmService service, HttpContext context) =>
{
    if (!RoleRules.CanAcknowledge(user.Current.Role)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var acknowledged = await service.AcknowledgeAsync(alarmId, user.Current, context.RequestAborted);
    if (acknowledged is null)
    {
        return Results.NotFound();
    }

    return Results.NoContent();
});

app.MapGet("/internal/v1/alarms/count", async (Guid unitId, string period, AlarmService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.CountForUnitPeriodAsync(unitId, period, cancellationToken)));

app.Run();
