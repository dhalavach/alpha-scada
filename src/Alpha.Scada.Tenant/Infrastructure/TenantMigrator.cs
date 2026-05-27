using Npgsql;

namespace Alpha.Scada.Tenant.Infrastructure;

public sealed class TenantMigrator(NpgsqlDataSource dataSource, ILogger<TenantMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            create table if not exists tenants (
                id uuid primary key,
                key text not null unique,
                name text not null,
                region text not null,
                created_at_utc timestamptz not null default now()
            );

            insert into tenants (id, key, name, region)
            values
              ('10000000-0000-0000-0000-000000000001', 'demo-operator', 'Demo Operator', 'EU'),
              ('10000000-0000-0000-0000-000000000002', 'field-operator', 'Field Operator', 'EU')
            on conflict (id) do nothing;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Tenant database is ready.");
    }
}
