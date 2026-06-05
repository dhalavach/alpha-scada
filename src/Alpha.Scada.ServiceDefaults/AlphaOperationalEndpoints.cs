/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/AlphaOperationalEndpoints.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlphaOperationalEndpoints
{
    public static IEndpointRouteBuilder MapAlphaOperationalEndpoints(this IEndpointRouteBuilder app, string serviceName)
    {
// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
        app.MapGet("/ready", (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            MinimalApi.ReadyAsync(dataSource, cancellationToken));
// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
        app.MapGet("/metrics", (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            MinimalApi.MetricsAsync(serviceName, dataSource, cancellationToken));

        return app;
    }
}
