/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Infrastructure/AssetMigrator.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file owns database schema creation and seed data for its service. In raw Npgsql systems this replaces an EF migration class.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Asset.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AssetMigrator(NpgsqlDataSource dataSource, ILogger<AssetMigrator> logger) :
    SqlDatabaseMigrator(dataSource, logger)
{
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
    [
        new("001_initial", """
            create table if not exists sites (
                id uuid primary key,
                tenant_id uuid not null,
                key text not null,
                name text not null,
                region text not null,
                status text not null default 'unknown',
                created_at_utc timestamptz not null default now(),
                unique (tenant_id, key)
            );

            create table if not exists units (
                id uuid primary key,
                tenant_id uuid not null,
                site_id uuid not null,
                key text not null,
                name text not null,
                model text not null,
                status text not null default 'unknown',
                last_seen_utc timestamptz,
                created_at_utc timestamptz not null default now(),
                unique (site_id, key)
            );

            insert into sites (id, tenant_id, key, name, region, status)
            values
              ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'demo-energy-site', 'Demo Energy Site', 'EU', 'online'),
              ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', 'field-energy-site', 'Field Energy Site', 'EU', 'online')
            on conflict (id) do nothing;

            insert into units (id, tenant_id, site_id, key, name, model, status)
            values
              ('30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', 'chp-demo-001', 'Combined Heat and Power Unit 001', 'Combined Heat and Power Unit', 'online'),
              ('30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000002', 'chp-field-001', 'Field Combined Heat and Power Unit', 'Combined Heat and Power Unit', 'online')
            on conflict (id) do nothing;

            create index if not exists ix_sites_tenant on sites(tenant_id);
            create index if not exists ix_units_tenant_site on units(tenant_id, site_id);
            """)
    ];
}
