/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Program.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Application.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Infrastructure;

// LEARN: declares a compile-time constant; callers cannot change this value.
const string serviceName = "alpha-scada-telemetry";

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaMigrator<TelemetryMigrator>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<TelemetryRepository>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<TelemetryService>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<CatalogCache>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<ITelemetryAdapter, NatsJsonTelemetryAdapter>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<TelemetryAdapterResolver>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<CanonicalTelemetryHandler>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddHostedService<TelemetryAdapterIngestionWorker>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddMemoryCache();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaServiceClients(
// LEARN: continues an argument/object/collection initializer onto the next line.
    builder.Configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Tenant,
// LEARN: continues an argument/object/collection initializer onto the next line.
    AlphaServiceClients.Asset,
// LEARN: executes one C# statement; semicolons terminate most statements.
    AlphaServiceClients.TagCatalog);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
// LEARN: enables the shared Wolverine/NATS messaging setup for this service.
builder.Host.UseAlphaMessaging(serviceName, MessagingTopology.Configure);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
var app = builder.Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
await app.ApplyAlphaMigrationsAsync();
// LEARN: executes one C# statement; semicolons terminate most statements.
app.UseAlphaAuthorization();
// LEARN: executes one C# statement; semicolons terminate most statements.
app.MapAlphaOperationalEndpoints(serviceName);

// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
var internalApi = app.MapGroup("/internal/v1").RequireAuthorization();

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
internalApi.MapGet("/telemetry/units/{unitId:guid}/current", async (Guid unitId, AuthenticatedUser user, TelemetryService service, HttpContext context) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await service.GetCurrentAsync(unitId, user.Current, context.RequestAborted)));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
internalApi.MapGet("/telemetry/tags/{tagId:guid}/history", async (Guid tagId, int? minutes, AuthenticatedUser user, TelemetryService service, HttpContext context) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var window = TimeSpan.FromMinutes(Math.Clamp(minutes ?? 30, 1, 24 * 60));
// LEARN: returns a value or exits the current method.
    return Results.Ok(await service.GetHistoryAsync(tagId, window, user.Current, context.RequestAborted));
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: registers an HTTP POST endpoint in ASP.NET Core Minimal APIs.
app.MapPost("/internal/v1/telemetry/units/{unitId:guid}/report-aggregate", async (Guid unitId, ReportAggregateRequest request, TelemetryService service, CancellationToken cancellationToken) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await service.GetReportAggregateAsync(unitId, request, cancellationToken)));

// LEARN: executes one C# statement; semicolons terminate most statements.
app.Run();
