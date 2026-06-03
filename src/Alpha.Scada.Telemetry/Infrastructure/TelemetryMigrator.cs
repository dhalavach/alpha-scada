using Alpha.Scada.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Alpha.Scada.Telemetry.Infrastructure;

public sealed class TelemetryMigrator : SqlDatabaseMigrator
{
    private const int DefaultRetentionDays = 365;
    private const int ChunkIntervalDays = 1;
    private const int CompressAfterDays = 7;

    public TelemetryMigrator(
        NpgsqlDataSource dataSource,
        IConfiguration configuration,
        ILogger<TelemetryMigrator> logger)
        : base(dataSource, logger)
    {
        Migrations = BuildMigrations(configuration);
    }

    public TelemetryMigrator(NpgsqlDataSource dataSource, ILogger<TelemetryMigrator> logger)
        : this(dataSource, new ConfigurationBuilder().Build(), logger)
    {
    }

    protected override IReadOnlyList<SqlMigration> Migrations { get; }

    protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var continuousAggregate = new NpgsqlCommand("""
            create materialized view if not exists telemetry_minute
            with (timescaledb.continuous, timescaledb.materialized_only = false) as
            select tag_id,
                   time_bucket(interval '1 minute', timestamp_utc) as minute_utc,
                   avg(value_double) as value_avg
            from telemetry_samples
            group by tag_id, minute_utc
            with no data;

            select add_continuous_aggregate_policy(
                'telemetry_minute',
                start_offset => interval '3 days',
                end_offset => interval '1 minute',
                schedule_interval => interval '1 minute',
                if_not_exists => true);
            """, connection);
        await continuousAggregate.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<SqlMigration> BuildMigrations(IConfiguration configuration)
    {
        var retentionDays = configuration.GetValue("Timescale:RetentionDays", DefaultRetentionDays);
        if (retentionDays <= 0)
        {
            throw new InvalidOperationException("Timescale:RetentionDays must be a positive integer.");
        }

        return
        [
            new("001_initial", """
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
                create index if not exists ix_telemetry_unit_time on telemetry_samples(unit_id, timestamp_utc desc);
                """),
            new("002_timescaledb", $$"""
                create extension if not exists timescaledb;

                alter table telemetry_samples rename to telemetry_samples_old;

                create table telemetry_samples (
                    tenant_id uuid not null,
                    unit_id uuid not null,
                    tag_id uuid not null,
                    tag_key text not null,
                    timestamp_utc timestamptz not null,
                    value_double double precision not null,
                    quality text not null,
                    source_timestamp_utc timestamptz not null,
                    received_at_utc timestamptz not null default now(),
                    constraint pk_telemetry_samples_timescale primary key (tag_id, timestamp_utc)
                );

                select create_hypertable(
                    'telemetry_samples',
                    'timestamp_utc',
                    chunk_time_interval => interval '{{ChunkIntervalDays}} day',
                    if_not_exists => true);

                insert into telemetry_samples (
                    tenant_id,
                    unit_id,
                    tag_id,
                    tag_key,
                    timestamp_utc,
                    value_double,
                    quality,
                    source_timestamp_utc,
                    received_at_utc)
                select tenant_id,
                       unit_id,
                       tag_id,
                       tag_key,
                       timestamp_utc,
                       value_double,
                       quality,
                       source_timestamp_utc,
                       received_at_utc
                from telemetry_samples_old;

                drop table telemetry_samples_old cascade;

                create index if not exists ix_telemetry_tenant_unit_time on telemetry_samples(tenant_id, unit_id, timestamp_utc desc);
                create index if not exists ix_telemetry_unit_time on telemetry_samples(unit_id, timestamp_utc desc);

                -- Tuned for 1 Hz pilot telemetry: daily chunks, compress chunks after a one-week hot window.
                alter table telemetry_samples set (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'tag_id',
                    timescaledb.compress_orderby = 'timestamp_utc desc'
                );
                select add_compression_policy('telemetry_samples', interval '{{CompressAfterDays}} days', if_not_exists => true);
                select add_retention_policy('telemetry_samples', interval '{{retentionDays}} days', if_not_exists => true);
                """)
        ];
    }
}
