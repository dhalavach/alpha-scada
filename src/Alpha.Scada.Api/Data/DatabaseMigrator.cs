using Alpha.Scada.Api.Modules.Auth;
using Npgsql;

namespace Alpha.Scada.Api.Data;

public sealed class DatabaseMigrator(NpgsqlDataSource dataSource, ILogger<DatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await SeedAsync(connection, cancellationToken);
        logger.LogInformation("Database schema is ready.");
    }

    private static async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var tenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondTenantId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var siteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var secondSiteId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var unitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var secondUnitId = Guid.Parse("30000000-0000-0000-0000-000000000002");

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, tx, """
            insert into tenants (id, key, name, region)
            values
              (@tenant_id, 'demo-operator', 'Demo Operator', 'EU'),
              (@second_tenant_id, 'field-operator', 'Field Operator', 'EU')
            on conflict (id) do nothing;

            insert into sites (id, tenant_id, key, name, region, status)
            values
              (@site_id, @tenant_id, 'demo-energy-site', 'Demo Energy Site', 'EU', 'online'),
              (@second_site_id, @second_tenant_id, 'field-energy-site', 'Field Energy Site', 'EU', 'online')
            on conflict (id) do nothing;

            insert into units (id, tenant_id, site_id, key, name, model, status)
            values
              (@unit_id, @tenant_id, @site_id, 'chp-demo-001', 'Combined Heat and Power Unit 001', 'Combined Heat and Power Unit', 'online'),
              (@second_unit_id, @second_tenant_id, @second_site_id, 'chp-field-001', 'Field Combined Heat and Power Unit', 'Combined Heat and Power Unit', 'online')
            on conflict (id) do nothing;
            """,
            new Dictionary<string, object?>
            {
                ["tenant_id"] = tenantId,
                ["second_tenant_id"] = secondTenantId,
                ["site_id"] = siteId,
                ["second_site_id"] = secondSiteId,
                ["unit_id"] = unitId,
                ["second_unit_id"] = secondUnitId
            },
            cancellationToken);

        foreach (var user in new[]
        {
            new { Id = Guid.Parse("40000000-0000-0000-0000-000000000001"), TenantId = tenantId, Email = "admin@alpha.local", DisplayName = "Platform Admin", Role = Roles.Admin, Password = "ChangeMe!123" },
            new { Id = Guid.Parse("40000000-0000-0000-0000-000000000002"), TenantId = tenantId, Email = "operator@alpha.local", DisplayName = "Platform Operator", Role = Roles.Operator, Password = "ChangeMe!123" },
            new { Id = Guid.Parse("40000000-0000-0000-0000-000000000003"), TenantId = tenantId, Email = "viewer@alpha.local", DisplayName = "Platform Viewer", Role = Roles.Viewer, Password = "ChangeMe!123" },
            new { Id = Guid.Parse("40000000-0000-0000-0000-000000000004"), TenantId = tenantId, Email = "support@alpha.local", DisplayName = "Platform Support", Role = Roles.SupportEngineer, Password = "ChangeMe!123" }
        })
        {
            await ExecuteAsync(connection, tx, """
                insert into users (id, tenant_id, email, display_name, password_hash, role)
                values (@id, @tenant_id, @email, @display_name, @password_hash, @role)
                on conflict (email) do nothing
                """,
                new Dictionary<string, object?>
                {
                    ["id"] = user.Id,
                    ["tenant_id"] = user.TenantId,
                    ["email"] = user.Email,
                    ["display_name"] = user.DisplayName,
                    ["password_hash"] = PasswordHasher.Hash(user.Password),
                    ["role"] = user.Role
                },
                cancellationToken);
        }

        foreach (var unit in new[] { (TenantId: tenantId, UnitId: unitId), (TenantId: secondTenantId, UnitId: secondUnitId) })
        {
            foreach (var tag in SeedTags)
            {
                await ExecuteAsync(connection, tx, """
                    insert into tags (id, tenant_id, unit_id, key, name, subsystem, engineering_unit, alarm_low, alarm_high)
                    values (gen_random_uuid(), @tenant_id, @unit_id, @key, @name, @subsystem, @unit, @alarm_low, @alarm_high)
                    on conflict (unit_id, key) do nothing
                    """,
                    new Dictionary<string, object?>
                    {
                        ["tenant_id"] = unit.TenantId,
                        ["unit_id"] = unit.UnitId,
                        ["key"] = tag.Key,
                        ["name"] = tag.Name,
                        ["subsystem"] = tag.Subsystem,
                        ["unit"] = tag.EngineeringUnit,
                        ["alarm_low"] = tag.AlarmLow,
                        ["alarm_high"] = tag.AlarmHigh
                    },
                    cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        foreach (var (key, value) in parameters)
        {
            command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static readonly SeedTag[] SeedTags =
    [
        new("fuel.wood_chip_feed_kg_h", "Wood chip feed", "Fuel Feed", "kg/h", 35, 75),
        new("gasifier.reactor_temp_c", "Gasifier reactor temperature", "Gasifier", "degC", 650, 950),
        new("gas_cleaning.filter_dp_mbar", "Filter differential pressure", "Gas Cleaning", "mbar", null, 85),
        new("engine.electrical_output_kw", "Electrical output", "Engine / Generator", "kW", 45, 65),
        new("engine.oil_reservoir_l", "Engine oil reservoir", "Engine / Generator", "L", 5, null),
        new("heat.thermal_output_kw", "Thermal output", "Heat Recovery", "kW", 90, 150),
        new("heat.supply_temp_c", "Hot water supply temperature", "Heat Recovery", "degC", 75, 98),
        new("heat.return_temp_c", "Cold water return temperature", "Heat Recovery", "degC", 42, 68),
        new("biochar.output_m3_day", "Biochar production estimate", "Biochar", "m3/day", 0.2, 1.0),
        new("exhaust.temperature_c", "Exhaust temperature", "Exhaust", "degC", null, 150),
        new("air.compressed_pressure_bar", "Compressed air pressure", "Compressed Air", "bar", 7.2, 9.2),
        new("ventilation.air_exchange_m3_h", "Air exchange", "Ventilation", "m3/h", 750, null),
        new("safety.negative_pressure_pa", "Negative pressure", "Safety", "Pa", -250, -20),
        new("safety.co_ppm", "CO concentration", "Safety", "ppm", null, 30),
        new("safety.fire_suppression_ready", "Fire suppression ready", "Safety", "state", 0.5, null)
    ];

    private const string SchemaSql = """
        create extension if not exists pgcrypto;

        do $$
        begin
            if exists (
                select 1
                from information_schema.columns
                where table_schema = 'public'
                  and table_name = 'units'
                  and column_name = 'id'
                  and data_type <> 'uuid'
            ) then
                drop table if exists alarm_events cascade;
                drop table if exists telemetry_samples cascade;
                drop table if exists tag_current cascade;
                drop table if exists tags cascade;
                drop table if exists units cascade;
            end if;
        end $$;

        create table if not exists tenants (
            id uuid primary key,
            key text not null unique,
            name text not null,
            region text not null,
            created_at_utc timestamptz not null default now()
        );

        create table if not exists sites (
            id uuid primary key,
            tenant_id uuid not null references tenants(id),
            key text not null,
            name text not null,
            region text not null,
            status text not null default 'unknown',
            created_at_utc timestamptz not null default now(),
            unique (tenant_id, key)
        );

        create table if not exists units (
            id uuid primary key,
            tenant_id uuid not null references tenants(id),
            site_id uuid not null references sites(id),
            key text not null,
            name text not null,
            model text not null,
            status text not null default 'unknown',
            last_seen_utc timestamptz,
            created_at_utc timestamptz not null default now(),
            unique (site_id, key)
        );

        create table if not exists tags (
            id uuid primary key,
            tenant_id uuid not null references tenants(id),
            unit_id uuid not null references units(id),
            key text not null,
            name text not null,
            subsystem text not null,
            engineering_unit text not null,
            alarm_low double precision,
            alarm_high double precision,
            unique (unit_id, key)
        );

        create table if not exists tag_current (
            tenant_id uuid not null references tenants(id),
            unit_id uuid not null references units(id),
            tag_id uuid primary key references tags(id),
            value_double double precision not null,
            quality text not null,
            timestamp_utc timestamptz not null
        );

        create table if not exists telemetry_samples (
            tenant_id uuid not null references tenants(id),
            unit_id uuid not null references units(id),
            tag_id uuid not null references tags(id),
            timestamp_utc timestamptz not null,
            value_double double precision not null,
            quality text not null,
            source_timestamp_utc timestamptz not null,
            received_at_utc timestamptz not null default now(),
            primary key (tag_id, timestamp_utc)
        ) partition by range (timestamp_utc);

        create table if not exists telemetry_samples_default partition of telemetry_samples default;

        create table if not exists alarm_events (
            id uuid primary key,
            tenant_id uuid not null references tenants(id),
            unit_id uuid not null references units(id),
            tag_id uuid references tags(id),
            severity text not null,
            message text not null,
            state text not null,
            raised_at_utc timestamptz not null,
            acknowledged_at_utc timestamptz,
            acknowledged_by_user_id uuid,
            cleared_at_utc timestamptz
        );

        create table if not exists users (
            id uuid primary key,
            tenant_id uuid not null references tenants(id),
            email text not null unique,
            display_name text not null,
            password_hash text not null,
            role text not null,
            created_at_utc timestamptz not null default now()
        );

        create table if not exists sessions (
            token_hash text primary key,
            user_id uuid not null references users(id),
            expires_at_utc timestamptz not null,
            created_at_utc timestamptz not null default now()
        );

        create table if not exists audit_events (
            id uuid primary key,
            tenant_id uuid references tenants(id),
            user_id uuid references users(id),
            event_type text not null,
            message text not null,
            created_at_utc timestamptz not null default now()
        );

        create table if not exists report_runs (
            id uuid primary key,
            tenant_id uuid not null references tenants(id),
            unit_id uuid not null references units(id),
            period text not null,
            electrical_kwh double precision not null,
            thermal_kwh double precision not null,
            runtime_hours double precision not null,
            availability_percent double precision not null,
            estimated_wood_chips_kg double precision not null,
            estimated_biochar_m3 double precision not null,
            alarm_count integer not null,
            generated_at_utc timestamptz not null,
            unique (unit_id, period)
        );

        create table if not exists edge_devices (
            id uuid primary key default gen_random_uuid(),
            tenant_id uuid not null references tenants(id),
            site_id uuid not null references sites(id),
            unit_id uuid not null references units(id),
            key text not null,
            credential_hash text,
            last_seen_utc timestamptz,
            unique (tenant_id, key)
        );

        create index if not exists ix_sites_tenant on sites(tenant_id);
        create index if not exists ix_units_tenant_site on units(tenant_id, site_id);
        create index if not exists ix_tags_tenant_unit on tags(tenant_id, unit_id);
        create index if not exists ix_telemetry_tenant_unit_time on telemetry_samples(tenant_id, unit_id, timestamp_utc desc);
        create index if not exists ix_alarm_tenant_state on alarm_events(tenant_id, state);
        """;

    private sealed record SeedTag(string Key, string Name, string Subsystem, string EngineeringUnit, double? AlarmLow, double? AlarmHigh);
}
