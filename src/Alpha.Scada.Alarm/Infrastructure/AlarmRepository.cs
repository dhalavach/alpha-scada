/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Infrastructure/AlarmRepository.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Domain;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;
using NpgsqlTypes;

namespace Alpha.Scada.Alarm.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmRepository
{
    private readonly NpgsqlDataSource dataSource;

    public AlarmRepository(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmChanges> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changes = await EvaluateAsync(connection, transaction, request, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
        return changes;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmChanges> EvaluateAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        AlarmEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var alarmingTagIds = new List<Guid>();
        var severities = new List<string>();
        var messages = new List<string>();
        var clearingTagIds = new List<Guid>();

// LEARN: loops over each item in a collection.
        foreach (var sample in request.Samples)
        {
            var result = AlarmRule.Evaluate(sample);
// LEARN: branches only when the boolean condition is true.
            if (result.IsAlarm)
            {
                alarmingTagIds.Add(sample.TagId);
                severities.Add(result.Severity);
                messages.Add(result.Message);
            }
// LEARN: runs when the preceding if/else-if conditions did not match.
            else
            {
                clearingTagIds.Add(sample.TagId);
            }
        }

        var raised = new List<AlarmDto>();
        var cleared = new List<AlarmDto>();
// LEARN: branches only when the boolean condition is true.
        if (alarmingTagIds.Count == 0 && clearingTagIds.Count == 0)
        {
            return new AlarmChanges(raised, cleared);
        }

// LEARN: branches only when the boolean condition is true.
        if (alarmingTagIds.Count > 0)
        {
            // One active alarm per tag: the partial unique index on (tag_id) where state in
            // ('active','acknowledged') makes the de-duplication atomic, so do nothing when a tag
            // already has an open alarm (including duplicate tags within this batch).
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var command = new NpgsqlCommand("""
                insert into alarm_events (id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc)
                select gen_random_uuid(), @tenant_id, @unit_id, a.tag_id, a.severity, a.message, 'active', now()
                from unnest(@tag_ids, @severities, @messages) as a(tag_id, severity, message)
                on conflict (tag_id) where state in ('active', 'acknowledged') do nothing
                returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
                """, connection, transaction);
            command.Parameters.AddWithValue("tenant_id", request.TenantId);
            command.Parameters.AddWithValue("unit_id", request.UnitId);
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
            command.Parameters.Add(new NpgsqlParameter("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = alarmingTagIds.ToArray() });
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
            command.Parameters.Add(new NpgsqlParameter("severities", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = severities.ToArray() });
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
            command.Parameters.Add(new NpgsqlParameter("messages", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = messages.ToArray() });
            raised.AddRange(await ReadAlarmsAsync(command, cancellationToken));

        }

// LEARN: branches only when the boolean condition is true.
        if (clearingTagIds.Count > 0)
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var command = new NpgsqlCommand("""
                update alarm_events
                set state = 'cleared', cleared_at_utc = now()
                where tag_id = any(@tag_ids) and state in ('active', 'acknowledged')
                returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
                """, connection, transaction);
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
            command.Parameters.Add(new NpgsqlParameter("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = clearingTagIds.ToArray() });
            cleared.AddRange(await ReadAlarmsAsync(command, cancellationToken));

        }

        return new AlarmChanges(raised, cleared);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await RaiseCommunicationLostAsync(connection, transaction, unit, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
        return alarm;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> RaiseCommunicationLostAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        UnitDto unit,
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<AlarmDto>> GetActiveAsync(CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> GetActiveAlarmAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var alarm = await AcknowledgeAsync(connection, transaction, alarmId, user, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
        return alarm;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> AcknowledgeAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        Guid alarmId,
        CurrentUserDto user,
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken)
    {
        var range = MonthPeriod.Parse(period);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task EnqueueOutboxAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        IEnumerable<object> events,
        CancellationToken cancellationToken)
    {
        var messages = events.Select(AlarmOutboxEvents.Serialize).ToArray();
// LEARN: branches only when the boolean condition is true.
        if (messages.Length == 0)
        {
            return;
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            insert into alarm_outbox (id, event_type, payload)
            select gen_random_uuid(), e.event_type, e.payload::jsonb
            from unnest(@event_types, @payloads) as e(event_type, payload)
            """, connection, transaction);
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        command.Parameters.Add(new NpgsqlParameter("event_types", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = messages.Select(message => message.EventType).ToArray()
        });
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        command.Parameters.Add(new NpgsqlParameter("payloads", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = messages.Select(message => message.Payload).ToArray()
        });
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyCollection<AlarmDto>> ReadAlarmsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<AlarmDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
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

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static async Task<AlarmDto?> ReadAlarmAsync(NpgsqlCommand command, CancellationToken cancellationToken) =>
        (await ReadAlarmsAsync(command, cancellationToken)).FirstOrDefault();
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmChanges(IReadOnlyCollection<AlarmDto> Raised, IReadOnlyCollection<AlarmDto> Cleared);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmRouteKeys(Guid TenantId, Guid UnitId, string TenantKey, string SiteKey, string UnitKey);
