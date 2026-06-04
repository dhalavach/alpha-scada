using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Alarm.Infrastructure;

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
