/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/AlphaOperationalEndpoints.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Builder;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Routing;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlphaOperationalEndpoints
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IEndpointRouteBuilder MapAlphaOperationalEndpoints(this IEndpointRouteBuilder app, string serviceName)
    {
// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
        app.MapGet("/ready", (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
            MinimalApi.ReadyAsync(dataSource, cancellationToken));
// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
        app.MapGet("/metrics", (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
            MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

// LEARN: returns a value or exits the current method.
        return app;
    }
}
