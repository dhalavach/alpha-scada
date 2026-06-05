/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.TagCatalog/Infrastructure/TagCatalogMigrator.cs
- Module role: Alpha.Scada.TagCatalog is the tag-catalog service. It owns tag definitions, engineering units, thresholds, subsystem grouping, and report ontology/configuration rather than scattering those constants through code.
- Local role: This file owns database schema creation and seed data for its service. In raw Npgsql systems this replaces an EF migration class.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.TagCatalog.Infrastructure;

public sealed class TagCatalogMigrator(NpgsqlDataSource dataSource, ILogger<TagCatalogMigrator> logger) :
    SqlDatabaseMigrator(dataSource, logger)
{
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
    [
        new("001_initial", """
            create extension if not exists pgcrypto;

            create table if not exists tags (
                id uuid primary key,
                tenant_id uuid not null,
                unit_id uuid not null,
                key text not null,
                name text not null,
                subsystem text not null,
                engineering_unit text not null,
                alarm_low double precision,
                alarm_high double precision,
                unique (unit_id, key)
            );

            create index if not exists ix_tags_tenant_unit on tags(tenant_id, unit_id);

            create table if not exists report_metric_definitions (
                metric_key text primary key,
                display_name text not null,
                aggregation_type text not null,
                output_unit text not null,
                default_scale double precision not null,
                default_threshold double precision
            );

            create table if not exists report_profiles (
                tenant_id uuid not null,
                unit_id uuid not null,
                availability_no_alarms_percent double precision not null,
                availability_with_alarms_percent double precision not null,
                biochar_yield_m3_per_kg double precision not null,
                primary key (tenant_id, unit_id)
            );

            create table if not exists report_metric_bindings (
                tenant_id uuid not null,
                unit_id uuid not null,
                metric_key text not null references report_metric_definitions(metric_key),
                tag_id uuid not null references tags(id),
                scale double precision,
                threshold double precision,
                primary key (tenant_id, unit_id, metric_key)
            );
            """)
    ];

    protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        foreach (var metric in ReportMetrics)
        {
            await using var definition = new NpgsqlCommand("""
                insert into report_metric_definitions (metric_key, display_name, aggregation_type, output_unit, default_scale, default_threshold)
                values (@metric_key, @display_name, @aggregation_type, @output_unit, @default_scale, @default_threshold)
                on conflict (metric_key) do update
                set display_name = excluded.display_name,
                    aggregation_type = excluded.aggregation_type,
                    output_unit = excluded.output_unit,
                    default_scale = excluded.default_scale,
                    default_threshold = excluded.default_threshold
                """, connection);
            definition.Parameters.AddWithValue("metric_key", metric.MetricKey);
            definition.Parameters.AddWithValue("display_name", metric.DisplayName);
            definition.Parameters.AddWithValue("aggregation_type", metric.AggregationType);
            definition.Parameters.AddWithValue("output_unit", metric.OutputUnit);
            definition.Parameters.AddWithValue("default_scale", metric.DefaultScale);
            definition.Parameters.AddWithValue("default_threshold", (object?)metric.DefaultThreshold ?? DBNull.Value);
            await definition.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var unit in Units)
        {
            foreach (var tag in SeedTags)
            {
                await using var seed = new NpgsqlCommand("""
                    insert into tags (id, tenant_id, unit_id, key, name, subsystem, engineering_unit, alarm_low, alarm_high)
                    values (gen_random_uuid(), @tenant_id, @unit_id, @key, @name, @subsystem, @unit, @alarm_low, @alarm_high)
                    on conflict (unit_id, key) do nothing
                    """, connection);
                seed.Parameters.AddWithValue("tenant_id", unit.TenantId);
                seed.Parameters.AddWithValue("unit_id", unit.UnitId);
                seed.Parameters.AddWithValue("key", tag.Key);
                seed.Parameters.AddWithValue("name", tag.Name);
                seed.Parameters.AddWithValue("subsystem", tag.Subsystem);
                seed.Parameters.AddWithValue("unit", tag.EngineeringUnit);
                seed.Parameters.AddWithValue("alarm_low", (object?)tag.AlarmLow ?? DBNull.Value);
                seed.Parameters.AddWithValue("alarm_high", (object?)tag.AlarmHigh ?? DBNull.Value);
                await seed.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var profile = new NpgsqlCommand("""
                insert into report_profiles (tenant_id, unit_id, availability_no_alarms_percent, availability_with_alarms_percent, biochar_yield_m3_per_kg)
                values (@tenant_id, @unit_id, 99.5, 98.5, 0.00045)
                on conflict (tenant_id, unit_id) do nothing
                """, connection);
            profile.Parameters.AddWithValue("tenant_id", unit.TenantId);
            profile.Parameters.AddWithValue("unit_id", unit.UnitId);
            await profile.ExecuteNonQueryAsync(cancellationToken);

            foreach (var metric in ReportMetrics)
            {
                await using var binding = new NpgsqlCommand("""
                    insert into report_metric_bindings (tenant_id, unit_id, metric_key, tag_id, scale, threshold)
                    select @tenant_id, @unit_id, @metric_key, t.id, null, null
                    from tags t
                    where t.tenant_id = @tenant_id and t.unit_id = @unit_id and t.key = @tag_key
                    on conflict (tenant_id, unit_id, metric_key) do nothing
                    """, connection);
                binding.Parameters.AddWithValue("tenant_id", unit.TenantId);
                binding.Parameters.AddWithValue("unit_id", unit.UnitId);
                binding.Parameters.AddWithValue("metric_key", metric.MetricKey);
                binding.Parameters.AddWithValue("tag_key", metric.DefaultTagKey);
                await binding.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static readonly (Guid TenantId, Guid UnitId)[] Units =
    [
        (Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("30000000-0000-0000-0000-000000000001")),
        (Guid.Parse("10000000-0000-0000-0000-000000000002"), Guid.Parse("30000000-0000-0000-0000-000000000002"))
    ];

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

    private sealed record SeedTag(string Key, string Name, string Subsystem, string EngineeringUnit, double? AlarmLow, double? AlarmHigh);

    private static readonly ReportMetricSeed[] ReportMetrics =
    [
        new("electrical_kwh", "Electrical energy", "sum_per_minute", "kWh", 1, null, "engine.electrical_output_kw"),
        new("thermal_kwh", "Thermal energy", "sum_per_minute", "kWh", 1, null, "heat.thermal_output_kw"),
        new("wood_chips_kg", "Wood-chip consumption", "sum_per_minute", "kg", 1, null, "fuel.wood_chip_feed_kg_h"),
        new("runtime_hours", "Runtime hours", "runtime_hours", "h", 1, 0, "engine.electrical_output_kw")
    ];

    private sealed record ReportMetricSeed(
        string MetricKey,
        string DisplayName,
        string AggregationType,
        string OutputUnit,
        double DefaultScale,
        double? DefaultThreshold,
        string DefaultTagKey);
}
