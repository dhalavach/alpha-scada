using Npgsql;

namespace Alpha.Scada.Alarm.Infrastructure;

public sealed class AlarmMigrator(NpgsqlDataSource dataSource, ILogger<AlarmMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await Alpha.Scada.ServiceDefaults.DomainOutbox.EnsureSchemaAsync(connection, cancellationToken);
        await using var command = new NpgsqlCommand("""
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
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Alarm database is ready.");
    }
}
