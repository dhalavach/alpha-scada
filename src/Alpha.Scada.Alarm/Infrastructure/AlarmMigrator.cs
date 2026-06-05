/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Infrastructure/AlarmMigrator.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file owns database schema creation and seed data for its service. In raw Npgsql systems this replaces an EF migration class.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Alarm.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmMigrator(NpgsqlDataSource dataSource, ILogger<AlarmMigrator> logger) :
    SqlDatabaseMigrator(dataSource, logger)
{
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
    [
        new("001_initial", """
            create extension if not exists pgcrypto;

            create table if not exists alarm_events (
                id uuid primary key,
                tenant_id uuid not null,
                unit_id uuid not null,
                tag_id uuid,
                severity text not null,
                message text not null,
                state text not null,
                raised_at_utc timestamptz not null,
                acknowledged_at_utc timestamptz,
                acknowledged_by_user_id uuid,
                cleared_at_utc timestamptz
            );

            create index if not exists ix_alarm_tenant_state on alarm_events(tenant_id, state);
            create index if not exists ix_alarm_unit_period on alarm_events(unit_id, raised_at_utc);
            create unique index if not exists ux_alarm_active_tag on alarm_events(tag_id) where state in ('active', 'acknowledged');
            """),

        new("002_alarm_outbox", """
            create table if not exists alarm_outbox (
                id uuid primary key,
                event_type text not null,
                payload jsonb not null,
                occurred_at_utc timestamptz not null default now(),
                dispatched_at_utc timestamptz,
                attempts int not null default 0,
                last_error text
            );

            create index if not exists ix_alarm_outbox_pending
                on alarm_outbox(occurred_at_utc)
                where dispatched_at_utc is null;
            """)
    ];
}
