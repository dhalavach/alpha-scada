/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Tenant/Infrastructure/TenantMigrator.cs
- Module role: Alpha.Scada.Tenant is the tenant registry. It is the source of truth for customer/operator records and tenant keys used to scope every downstream asset, tag, telemetry, alarm, and report query.
- Local role: This file owns database schema creation and seed data for its service. In raw Npgsql systems this replaces an EF migration class.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Tenant.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TenantMigrator(NpgsqlDataSource dataSource, ILogger<TenantMigrator> logger) :
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

            insert into tenants (id, key, name, region)
            values
              ('10000000-0000-0000-0000-000000000001', 'demo-operator', 'Demo Operator', 'EU'),
              ('10000000-0000-0000-0000-000000000002', 'field-operator', 'Field Operator', 'EU')
            on conflict (id) do nothing;
            """)
    ];
}
