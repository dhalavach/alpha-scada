/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Database.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class Database
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IServiceCollection AddServiceDatabase(this IServiceCollection services, IConfiguration configuration)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var connectionString = configuration.GetConnectionString("Postgres")
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
// LEARN: returns a value or exits the current method.
        return services;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static async Task<bool> CanConnectAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("select 1", connection);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteScalarAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return true;
    }
}
