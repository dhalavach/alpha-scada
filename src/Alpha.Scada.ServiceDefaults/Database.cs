/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Database.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class Database
{
    public static IServiceCollection AddServiceDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        return services;
    }

    public static async Task<bool> CanConnectAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("select 1", connection);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteScalarAsync(cancellationToken);
        return true;
    }
}
