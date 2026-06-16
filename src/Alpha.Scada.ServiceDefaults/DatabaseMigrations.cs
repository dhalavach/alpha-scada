using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Alpha.Scada.ServiceDefaults;

public interface IDatabaseMigrator
{
    string Name { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

public sealed record SqlMigration(string Id, string Sql);

public abstract class SqlDatabaseMigrator(
    NpgsqlDataSource dataSource,
    ILogger logger) : IDatabaseMigrator
{
    public virtual string Name => GetType().Name;

    protected abstract IReadOnlyList<SqlMigration> Migrations { get; }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            "select pg_advisory_xact_lock(hashtext('alpha_schema_migrations'));",
            cancellationToken);
        await ExecuteAsync(connection, transaction, """
            create table if not exists alpha_schema_migrations (
                migrator text not null,
                migration_id text not null,
                applied_at_utc timestamptz not null default now(),
                primary key (migrator, migration_id)
            );
            """, cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            "select pg_advisory_xact_lock(hashtext(@lock_key));",
            cancellationToken,
            ("lock_key", Name));

        foreach (var migration in Migrations)
        {
            if (await HasAppliedAsync(connection, transaction, migration.Id, cancellationToken))
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken);
            await ExecuteAsync(
                connection,
                transaction,
                "insert into alpha_schema_migrations (migrator, migration_id) values (@migrator, @migration_id);",
                cancellationToken,
                ("migrator", Name),
                ("migration_id", migration.Id));
            logger.LogInformation("Applied migration {Migrator}/{MigrationId}.", Name, migration.Id);
        }

        await transaction.CommitAsync(cancellationToken);
        await SeedAsync(connection, cancellationToken);
        logger.LogInformation("{Migrator} database is ready.", Name);
    }

    protected virtual Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private async Task<bool> HasAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
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
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

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
        foreach (var migrator in migrators)
        {
            await migrator.MigrateAsync(cancellationToken);
        }
    }
}
