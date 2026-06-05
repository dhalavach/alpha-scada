/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Program.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Alarm;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;

const string serviceName = "alpha-scada-alarm";

var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaMigrator<AlarmMigrator>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<AlarmRepository>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<AlarmService>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<AlarmOutboxDispatcher>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<IAlarmOutboxSignal>(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddHostedService(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<UnitKeyResolver>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<ThresholdCache>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddMemoryCache();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaServiceClients(
    builder.Configuration,
    AlphaServiceClients.Asset,
    AlphaServiceClients.Tenant,
    AlphaServiceClients.TagCatalog);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
// LEARN: enables the shared Wolverine/NATS messaging setup for this service.
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

var app = builder.Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

internalApi.MapGet("/alarms/active", async (AuthenticatedUser user, AlarmService service, HttpContext context) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await service.GetActiveAsync(user.Current, context.RequestAborted)));

internalApi.MapPost("/alarms/{alarmId:guid}/ack", async (Guid alarmId, AuthenticatedUser user, AlarmService service, HttpContext context) =>
{
// LEARN: branches only when the boolean condition is true.
    if (!RoleRules.CanAcknowledge(user.Current.Role)) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var acknowledged = await service.AcknowledgeAsync(alarmId, user.Current, context.RequestAborted);
// LEARN: branches only when the boolean condition is true.
    if (acknowledged is null)
    {
        return Results.NotFound();
    }

    return Results.NoContent();
});

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
app.MapGet("/internal/v1/alarms/count", async (Guid unitId, string period, AlarmService service, CancellationToken cancellationToken) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await service.CountForUnitPeriodAsync(unitId, period, cancellationToken)));

app.Run();
