/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/DatabaseMigrations.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Builder;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Logging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares an interface, a contract that implementations agree to satisfy.
public interface IDatabaseMigrator
{
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    string Name { get; }

// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
    Task MigrateAsync(CancellationToken cancellationToken);
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record SqlMigration(string Id, string Sql);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
public abstract class SqlDatabaseMigrator(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    ILogger logger) : IDatabaseMigrator
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public virtual string Name => GetType().Name;

// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    protected abstract IReadOnlyList<SqlMigration> Migrations { get; }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await ExecuteAsync(connection, transaction, """
            create table if not exists alpha_schema_migrations (
                migrator text not null,
                migration_id text not null,
                applied_at_utc timestamptz not null default now(),
                primary key (migrator, migration_id)
            );
            """, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await ExecuteAsync(connection, transaction, "select pg_advisory_xact_lock(hashtext(@lock_key));", cancellationToken, ("lock_key", Name));

// LEARN: loops over each item in a collection.
        foreach (var migration in Migrations)
        {
// LEARN: branches only when the boolean condition is true.
            if (await HasAppliedAsync(connection, transaction, migration.Id, cancellationToken))
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                continue;
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await ExecuteAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                connection,
// LEARN: continues an argument/object/collection initializer onto the next line.
                transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
                "insert into alpha_schema_migrations (migrator, migration_id) values (@migrator, @migration_id);",
// LEARN: continues an argument/object/collection initializer onto the next line.
                cancellationToken,
// LEARN: continues an argument/object/collection initializer onto the next line.
                ("migrator", Name),
// LEARN: executes one C# statement; semicolons terminate most statements.
                ("migration_id", migration.Id));
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogInformation("Applied migration {Migrator}/{MigrationId}.", Name, migration.Id);
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await SeedAsync(connection, cancellationToken);
// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogInformation("{Migrator} database is ready.", Name);
    }

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
    protected virtual Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        Task.CompletedTask;

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<bool> HasAppliedAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string migrationId,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1
                from alpha_schema_migrations
                where migrator = @migrator and migration_id = @migration_id
            );
            """, connection, transaction);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("migrator", Name);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("migration_id", migrationId);
// LEARN: returns a value or exits the current method.
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task ExecuteAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string sql,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        params (string Name, object? Value)[] parameters)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(sql, connection, transaction);
// LEARN: loops over each item in a collection.
        foreach (var parameter in parameters)
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

// LEARN: declares a static helper class whose members are called on the type itself.
public static class DatabaseMigrationRegistration
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IServiceCollection AddAlphaMigrator<TMigrator>(this IServiceCollection services)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        where TMigrator : class, IDatabaseMigrator
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<TMigrator>();
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        services.AddSingleton<IDatabaseMigrator>(provider => provider.GetRequiredService<TMigrator>());
// LEARN: returns a value or exits the current method.
        return services;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static async Task ApplyAlphaMigrationsAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var migrators = app.Services.GetServices<IDatabaseMigrator>();
// LEARN: loops over each item in a collection.
        foreach (var migrator in migrators)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await migrator.MigrateAsync(cancellationToken);
        }
    }
}
