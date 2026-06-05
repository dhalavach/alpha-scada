/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Tenant/Program.cs
- Module role: Alpha.Scada.Tenant is the tenant registry. It is the source of truth for customer/operator records and tenant keys used to scope every downstream asset, tag, telemetry, alarm, and report query.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Tenant.Application;
using Alpha.Scada.Tenant.Infrastructure;

const string serviceName = "alpha-scada-tenant";

var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaMigrator<TenantMigrator>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<TenantRepository>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<TenantService>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);

var app = builder.Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

app.MapGroup("/internal/v1")
// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
    .RequireAuthorization()
    .MapGet("/tenants", async (AuthenticatedUser user, TenantService service, HttpContext context) =>
// LEARN: creates an ASP.NET Core HTTP response result.
        Results.Ok(await service.GetTenantsAsync(user.Current, context.RequestAborted)));

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
app.MapGet("/internal/v1/tenants/resolve/{tenantKey}", async (string tenantKey, TenantService service, CancellationToken cancellationToken) =>
{
    var tenant = await service.ResolveAsync(tenantKey, cancellationToken);
    return tenant is null ? Results.NotFound() : Results.Ok(tenant);
});

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
app.MapGet("/internal/v1/tenants/{tenantId:guid}", async (Guid tenantId, TenantService service, CancellationToken cancellationToken) =>
{
    var tenant = await service.GetByIdAsync(tenantId, cancellationToken);
    return tenant is null ? Results.NotFound() : Results.Ok(tenant);
});

app.Run();
