/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Identity/Program.cs
- Module role: Alpha.Scada.Identity is the identity service. It owns local users, password hashing, role assignment, and JWT issuance, so other services can trust bearer-token claims instead of duplicating credential logic.
- Local role: This is the composition root: it wires configuration, dependency injection, authentication, messaging, migrations, operational endpoints, and HTTP routes for one process.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Application;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.ServiceDefaults;

const string serviceName = "alpha-scada-identity";

var builder = WebApplication.CreateBuilder(args);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddServiceDatabase(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaMigrator<IdentityMigrator>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<IdentityRepository>();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddAlphaJwtAuthentication(builder.Configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
builder.Services.AddSingleton<AuthService>();

var app = builder.Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
await app.ApplyAlphaMigrationsAsync();
app.UseAlphaAuthorization();
app.MapAlphaOperationalEndpoints(serviceName);

// LEARN: registers an HTTP POST endpoint in ASP.NET Core Minimal APIs.
app.MapPost("/internal/v1/auth/login", async (LoginRequest request, AuthService auth, CancellationToken cancellationToken) =>
{
    var response = await auth.LoginAsync(request, cancellationToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
});

app.MapGroup("/internal/v1")
// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
    .RequireAuthorization()
    .MapPost("/auth/logout", async (AuthenticatedUser user, IdentityRepository repository, HttpContext context) =>
    {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await repository.WriteAuditAsync(
            user.Current.TenantId,
            user.Current.UserId,
            "auth.logout",
            "User logged out",
            context.RequestAborted);
        return Results.NoContent();
    });

app.Run();
