using Alpha.Scada.Alarm.Domain;
using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.Alarm.Infrastructure;

public sealed class AlarmRepository(NpgsqlDataSource dataSource)
{
    public async Task<AlarmChanges> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        var raised = new List<AlarmDto>();
        var cleared = new List<AlarmDto>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var sample in request.Samples)
        {
            var result = AlarmRule.Evaluate(sample);
            if (result.IsAlarm)
            {
                await using var command = new NpgsqlCommand("""
                    insert into alarm_events (id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc)
                    select gen_random_uuid(), @tenant_id, @unit_id, @tag_id, @severity, @message, 'active', now()
                    where not exists (
                        select 1 from alarm_events
                        where tag_id = @tag_id and state in ('active', 'acknowledged')
                    )
                    returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
                    """, connection);
                command.Parameters.AddWithValue("tenant_id", request.TenantId);
                command.Parameters.AddWithValue("unit_id", request.UnitId);
                command.Parameters.AddWithValue("tag_id", sample.TagId);
                command.Parameters.AddWithValue("severity", result.Severity);
                command.Parameters.AddWithValue("message", result.Message);
                var alarm = await ReadAlarmAsync(command, cancellationToken);
                if (alarm is not null)
                {
                    raised.Add(alarm);
                }
            }
            else
            {
                await using var command = new NpgsqlCommand("""
                    update alarm_events
                    set state = 'cleared', cleared_at_utc = now()
                    where tag_id = @tag_id and state in ('active', 'acknowledged')
                    returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
                    """, connection);
                command.Parameters.AddWithValue("tag_id", sample.TagId);
                cleared.AddRange(await ReadAlarmsAsync(command, cancellationToken));
            }
        }

        return new AlarmChanges(raised, cleared);
    }

    public async Task<AlarmDto?> RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            insert into alarm_events (id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc)
            select gen_random_uuid(), @tenant_id, @unit_id, null, 'critical', @message, 'active', now()
            where not exists (
                select 1 from alarm_events
                where unit_id = @unit_id and tag_id is null and state in ('active', 'acknowledged')
            )
            returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            """, connection);
        command.Parameters.AddWithValue("tenant_id", unit.TenantId);
        command.Parameters.AddWithValue("unit_id", unit.Id);
        command.Parameters.AddWithValue("message", $"{unit.Name} communication lost");
        return await ReadAlarmAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            from alarm_events
            where state in ('active', 'acknowledged') and (@is_support or tenant_id = @tenant_id)
            order by raised_at_utc desc
            """, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return await ReadAlarmsAsync(command, cancellationToken);
    }

    public async Task<AlarmDto?> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update alarm_events
            set state = 'acknowledged', acknowledged_at_utc = now(), acknowledged_by_user_id = @user_id
            where id = @alarm_id and state = 'active' and (@is_support or tenant_id = @tenant_id)
            returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            """, connection);
        command.Parameters.AddWithValue("alarm_id", alarmId);
        command.Parameters.AddWithValue("user_id", user.UserId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return await ReadAlarmAsync(command, cancellationToken);
    }

    public async Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select count(*)
            from alarm_events
            where unit_id = @unit_id and to_char(raised_at_utc, 'YYYY-MM') = @period
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("period", period);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyCollection<AlarmDto>> ReadAlarmsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<AlarmDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AlarmDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return results;
    }

    private static async Task<AlarmDto?> ReadAlarmAsync(NpgsqlCommand command, CancellationToken cancellationToken) =>
        (await ReadAlarmsAsync(command, cancellationToken)).FirstOrDefault();
}

public sealed record AlarmChanges(IReadOnlyCollection<AlarmDto> Raised, IReadOnlyCollection<AlarmDto> Cleared);
