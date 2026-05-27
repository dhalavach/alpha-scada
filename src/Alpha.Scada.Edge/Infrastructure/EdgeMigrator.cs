using Npgsql;

namespace Alpha.Scada.Edge.Infrastructure;

public sealed class EdgeMigrator(NpgsqlDataSource dataSource, ILogger<EdgeMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            create extension if not exists pgcrypto;

            create table if not exists edge_devices (
                id uuid primary key default gen_random_uuid(),
                tenant_id uuid not null,
                site_id uuid not null,
                unit_id uuid not null,
                key text not null,
                credential_hash text,
                last_seen_utc timestamptz,
                unique (tenant_id, key)
            );
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Edge database is ready.");
    }
}
