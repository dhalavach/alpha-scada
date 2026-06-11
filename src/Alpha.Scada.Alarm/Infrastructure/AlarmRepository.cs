using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Domain;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;
using NpgsqlTypes;

namespace Alpha.Scada.Alarm.Infrastructure;

public sealed class AlarmRepository(NpgsqlDataSource dataSource)
{

    public async Task<AlarmChanges> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changes = await EvaluateAsync(connection, transaction, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changes;
    }

    public async Task<AlarmChanges> EvaluateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AlarmEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var alarmingTagIds = new List<Guid>();
        var severities = new List<string>();
        var messages = new List<string>();
        var clearingTagIds = new List<Guid>();

        foreach (var sample in request.Samples)
        {
            var result = AlarmRule.Evaluate(sample);
            if (result.IsAlarm)
            {
                alarmingTagIds.Add(sample.TagId);
                severities.Add(result.Severity);
                messages.Add(result.Message);
            }
            else
            {
                clearingTagIds.Add(sample.TagId);
            }
        }

        var raised = new List<AlarmDto>();
        var cleared = new List<AlarmDto>();
        if (alarmingTagIds.Count == 0 && clearingTagIds.Count == 0)
        {
            return new AlarmChanges(raised, cleared);
        }

        if (alarmingTagIds.Count > 0)
        {
            // One active alarm per tag: the partial unique index on (tag_id) where state in
            // ('active','acknowledged') makes the de-duplication atomic, so do nothing when a tag
            // already has an open alarm (including duplicate tags within this batch).
            await using var command = new NpgsqlCommand("""
                insert into alarm_events (id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc)
                select gen_random_uuid(), @tenant_id, @unit_id, a.tag_id, a.severity, a.message, 'active', now()
                from unnest(@tag_ids, @severities, @messages) as a(tag_id, severity, message)
                on conflict (tag_id) where state in ('active', 'acknowledged') do nothing
                returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
                """, connection, transaction);
            command.Parameters.AddWithValue("tenant_id", request.TenantId);
            command.Parameters.AddWithValue("unit_id", request.UnitId);
            command.Parameters.Add(new NpgsqlParameter("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = alarmingTagIds.ToArray() });
            command.Parameters.Add(new NpgsqlParameter("severities", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = severities.ToArray() });
            command.Parameters.Add(new NpgsqlParameter("messages", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = messages.ToArray() });
            raised.AddRange(await ReadAlarmsAsync(command, cancellationToken));

        }

        if (clearingTagIds.Count > 0)
        {
            await using var command = new NpgsqlCommand("""
                update alarm_events
                set state = 'cleared', cleared_at_utc = now()
                where tag_id = any(@tag_ids) and state in ('active', 'acknowledged')
                returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
                """, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = clearingTagIds.ToArray() });
            cleared.AddRange(await ReadAlarmsAsync(command, cancellationToken));

        }

        return new AlarmChanges(raised, cleared);
    }

    public async Task<AlarmDto?> RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await RaiseCommunicationLostAsync(connection, transaction, unit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return alarm;
    }

    public async Task<AlarmDto?> RaiseCommunicationLostAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        UnitDto unit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into alarm_events (id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc)
            select gen_random_uuid(), @tenant_id, @unit_id, null, 'critical', @message, 'active', now()
            where not exists (
                select 1 from alarm_events
                where unit_id = @unit_id and tag_id is null and state in ('active', 'acknowledged')
            )
            returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", unit.TenantId);
        command.Parameters.AddWithValue("unit_id", unit.Id);
        command.Parameters.AddWithValue("message", $"{unit.Name} communication lost");
        return await ReadAlarmAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyCollection<AlarmDto>> ClearCommunicationLostAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            update alarm_events
            set state = 'cleared', cleared_at_utc = now()
            where unit_id = @unit_id and tag_id is null and state in ('active', 'acknowledged')
            returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            """, connection, transaction);
        command.Parameters.AddWithValue("unit_id", unitId);
        return await ReadAlarmsAsync(command, cancellationToken);
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

    public async Task<AlarmDto?> GetActiveAlarmAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            from alarm_events
            where id = @alarm_id and state = 'active' and (@is_support or tenant_id = @tenant_id)
            """, connection);
        command.Parameters.AddWithValue("alarm_id", alarmId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return await ReadAlarmAsync(command, cancellationToken);
    }

    public async Task<AlarmDto?> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await AcknowledgeAsync(connection, transaction, alarmId, user, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return alarm;
    }

    public async Task<AlarmDto?> AcknowledgeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid alarmId,
        CurrentUserDto user,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            update alarm_events
            set state = 'acknowledged', acknowledged_at_utc = now(), acknowledged_by_user_id = @user_id
            where id = @alarm_id and state = 'active' and (@is_support or tenant_id = @tenant_id)
            returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            """, connection, transaction);
        command.Parameters.AddWithValue("alarm_id", alarmId);
        command.Parameters.AddWithValue("user_id", user.UserId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return await ReadAlarmAsync(command, cancellationToken);
    }

    public async Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken)
    {
        var range = MonthPeriod.Parse(period);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select count(*)
            from alarm_events
            where unit_id = @unit_id
              and raised_at_utc >= @period_start
              and raised_at_utc < @period_end
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("period_start", range.StartUtc);
        command.Parameters.AddWithValue("period_end", range.EndUtc);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task EnqueueOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<object> events,
        CancellationToken cancellationToken)
    {
        var messages = events.Select(AlarmOutboxEvents.Serialize).ToArray();
        if (messages.Length == 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand("""
            insert into alarm_outbox (id, event_type, payload)
            select gen_random_uuid(), e.event_type, e.payload::jsonb
            from unnest(@event_types, @payloads) as e(event_type, payload)
            """, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("event_types", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = messages.Select(message => message.EventType).ToArray()
        });
        command.Parameters.Add(new NpgsqlParameter("payloads", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = messages.Select(message => message.Payload).ToArray()
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
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

public sealed record AlarmRouteKeys(Guid TenantId, Guid UnitId, string TenantKey, string SiteKey, string UnitKey);
