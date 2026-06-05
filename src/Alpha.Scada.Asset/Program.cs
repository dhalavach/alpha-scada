/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Program.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a compile-time constant; callers cannot change this value.
const string serviceName = "alpha-scada-asset";

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaMigrator<AssetMigrator>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<AssetRepository>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<AssetService>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<TenantKeyResolver>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddMemoryCache();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddHostedService<CommunicationLossMonitorWorker>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaServiceClients(builder.Configuration, AlphaServiceClients.Tenant);
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
internalApi.MapGet("/sites", async (AuthenticatedUser user, AssetService service, HttpContext context) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await service.GetSitesAsync(user.Current, context.RequestAborted)));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
internalApi.MapGet("/sites/{siteId:guid}/units", async (Guid siteId, AuthenticatedUser user, AssetService service, HttpContext context) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await service.GetUnitsForSiteAsync(siteId, user.Current, context.RequestAborted)));

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
internalApi.MapGet("/units/{unitId:guid}", async (Guid unitId, AuthenticatedUser user, AssetService service, HttpContext context) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var unit = await service.GetUnitAsync(unitId, user.Current, context.RequestAborted);
// LEARN: returns a value or exits the current method.
    return unit is null ? Results.NotFound() : Results.Ok(unit);
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
app.MapGet("/internal/v1/units/resolve", async (Guid tenantId, string siteKey, string unitKey, AssetService service, CancellationToken cancellationToken) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var unit = await service.ResolveUnitAsync(tenantId, siteKey, unitKey, cancellationToken);
// LEARN: returns a value or exits the current method.
    return unit is null ? Results.NotFound() : Results.Ok(unit);
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
app.MapGet("/internal/v1/units/{unitId:guid}/route", async (Guid unitId, AssetService service, CancellationToken cancellationToken) =>
{
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
    var unit = await service.GetUnitRouteAsync(unitId, cancellationToken);
// LEARN: returns a value or exits the current method.
    return unit is null ? Results.NotFound() : Results.Ok(unit);
// LEARN: executes one C# statement; semicolons terminate most statements.
});

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
app.MapGet("/internal/v1/units/stale", async (int? minutes, AssetService service, CancellationToken cancellationToken) =>
// LEARN: creates an ASP.NET Core HTTP response result.
    Results.Ok(await service.GetStaleUnitsAsync(minutes ?? 2, cancellationToken)));

// LEARN: executes one C# statement; semicolons terminate most statements.
app.Run();
