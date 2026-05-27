using Npgsql;

namespace Alpha.Scada.TagCatalog.Infrastructure;

public sealed class TagCatalogMigrator(NpgsqlDataSource dataSource, ILogger<TagCatalogMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
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
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

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
        }

        logger.LogInformation("Tag catalog database is ready.");
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
}
