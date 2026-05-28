using Npgsql;

namespace Alpha.Scada.Telemetry.Infrastructure;

public sealed class TelemetryMigrator(NpgsqlDataSource dataSource, ILogger<TelemetryMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            create table if not exists tag_current (
                tenant_id uuid not null,
                unit_id uuid not null,
                tag_id uuid primary key,
                tag_key text not null,
                value_double double precision not null,
                quality text not null,
                timestamp_utc timestamptz not null
            );

            create table if not exists telemetry_samples (
                tenant_id uuid not null,
                unit_id uuid not null,
                tag_id uuid not null,
                tag_key text not null,
                timestamp_utc timestamptz not null,
                value_double double precision not null,
                quality text not null,
                source_timestamp_utc timestamptz not null,
                received_at_utc timestamptz not null default now(),
                primary key (tag_id, timestamp_utc)
            ) partition by range (timestamp_utc);

            create table if not exists telemetry_samples_default partition of telemetry_samples default;
            create index if not exists ix_telemetry_tenant_unit_time on telemetry_samples(tenant_id, unit_id, timestamp_utc desc);

            create table if not exists tag_current_shadow (
                tenant_id uuid not null,
                unit_id uuid not null,
                tag_id uuid primary key,
                tag_key text not null,
                value_double double precision not null,
                quality text not null,
                timestamp_utc timestamptz not null
            );

            create table if not exists telemetry_samples_shadow (
                tenant_id uuid not null,
                unit_id uuid not null,
                tag_id uuid not null,
                tag_key text not null,
                timestamp_utc timestamptz not null,
                value_double double precision not null,
                quality text not null,
                source_timestamp_utc timestamptz not null,
                received_at_utc timestamptz not null default now(),
                primary key (tag_id, timestamp_utc)
            ) partition by range (timestamp_utc);

            create table if not exists telemetry_samples_shadow_default partition of telemetry_samples_shadow default;
            create index if not exists ix_telemetry_shadow_tenant_unit_time on telemetry_samples_shadow(tenant_id, unit_id, timestamp_utc desc);
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Telemetry database is ready.");
    }
}
