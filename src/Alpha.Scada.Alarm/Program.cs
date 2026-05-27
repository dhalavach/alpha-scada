using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-alarm";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDatabase(builder.Configuration);
builder.Services.AddSingleton<AlarmMigrator>();
builder.Services.AddSingleton<AlarmRepository>();
builder.Services.AddSingleton<AlarmService>();
builder.Services.AddSingleton<JwtTokenService>();

var app = builder.Build();
await app.Services.GetRequiredService<AlarmMigrator>().MigrateAsync(CancellationToken.None);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
app.MapGet("/ready", MinimalApi.ReadyAsync);
app.MapGet("/metrics", () => MinimalApi.Metrics(serviceName));

app.MapPost("/internal/v1/alarms/evaluate", async (AlarmEvaluationRequest request, AlarmService service, CancellationToken cancellationToken) =>
{
    await service.EvaluateAsync(request, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/internal/v1/alarms/communication-lost", async (UnitDto unit, AlarmService service, CancellationToken cancellationToken) =>
{
    await service.RaiseCommunicationLostAsync(unit, cancellationToken);
    return Results.NoContent();
});

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
