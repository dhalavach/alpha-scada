using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Tenant.Infrastructure;

public sealed class TenantMigrator(
    NpgsqlDataSource dataSource,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<TenantMigrator> logger) :
    SqlDatabaseMigrator(dataSource, logger)
{
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
    [
        new("001_initial", """
            create table if not exists tenants (
                id uuid primary key,
                key text not null unique,
                name text not null,
                region text not null,
                created_at_utc timestamptz not null default now()
            );
            """)
    ];

    protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (!(configuration.GetValue<bool?>("Seed:DemoData") ?? environment.IsDevelopment()))
        {
            return;
        }

        await using var seed = new NpgsqlCommand("""
            insert into tenants (id, key, name, region)
            values
              ('10000000-0000-0000-0000-000000000001', 'demo-operator', 'Demo Operator', 'EU'),
              ('10000000-0000-0000-0000-000000000002', 'field-operator', 'Field Operator', 'EU')
            on conflict (id) do nothing
            """, connection);
        await seed.ExecuteNonQueryAsync(cancellationToken);
    }
}
