/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Infrastructure/TelemetryMigrator.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file owns database schema creation and seed data for its service. In raw Npgsql systems this replaces an EF migration class.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryMigrator : SqlDatabaseMigrator
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private const int DefaultRetentionDays = 365;
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private const int ChunkIntervalDays = 1;
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private const int CompressAfterDays = 7;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public TelemetryMigrator(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlDataSource dataSource,
// LEARN: continues an argument/object/collection initializer onto the next line.
        IConfiguration configuration,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        ILogger<TelemetryMigrator> logger)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        : base(dataSource, logger)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        Migrations = BuildMigrations(configuration);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public TelemetryMigrator(NpgsqlDataSource dataSource, ILogger<TelemetryMigrator> logger)
// LEARN: creates a new object or record instance.
        : this(dataSource, new ConfigurationBuilder().Build(), logger)
    {
    }

// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    protected override IReadOnlyList<SqlMigration> Migrations { get; }

// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await continuousAggregate.ExecuteNonQueryAsync(cancellationToken);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static IReadOnlyList<SqlMigration> BuildMigrations(IConfiguration configuration)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var retentionDays = configuration.GetValue("Timescale:RetentionDays", DefaultRetentionDays);
// LEARN: branches only when the boolean condition is true.
        if (retentionDays <= 0)
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException("Timescale:RetentionDays must be a positive integer.");
        }

// LEARN: returns a value or exits the current method.
        return
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        [
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
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
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
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
