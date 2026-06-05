/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/DatabaseMigrations.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares an interface, a contract that implementations agree to satisfy.
public interface IDatabaseMigrator
{
    string Name { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record SqlMigration(string Id, string Sql);

public abstract class SqlDatabaseMigrator(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource,
    ILogger logger) : IDatabaseMigrator
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public virtual string Name => GetType().Name;

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
                continue;
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await ExecuteAsync(
                connection,
                transaction,
                "insert into alpha_schema_migrations (migrator, migration_id) values (@migrator, @migration_id);",
                cancellationToken,
                ("migrator", Name),
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

    protected virtual Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        Task.CompletedTask;

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<bool> HasAppliedAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        string migrationId,
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
        command.Parameters.AddWithValue("migrator", Name);
        command.Parameters.AddWithValue("migration_id", migrationId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ExecuteAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(sql, connection, transaction);
// LEARN: loops over each item in a collection.
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

// LEARN: declares a static helper class whose members are called on the type itself.
public static class DatabaseMigrationRegistration
{
    public static IServiceCollection AddAlphaMigrator<TMigrator>(this IServiceCollection services)
        where TMigrator : class, IDatabaseMigrator
    {
        services.AddSingleton<TMigrator>();
        services.AddSingleton<IDatabaseMigrator>(provider => provider.GetRequiredService<TMigrator>());
        return services;
    }

    public static async Task ApplyAlphaMigrationsAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        var migrators = app.Services.GetServices<IDatabaseMigrator>();
// LEARN: loops over each item in a collection.
        foreach (var migrator in migrators)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await migrator.MigrateAsync(cancellationToken);
        }
    }
}
