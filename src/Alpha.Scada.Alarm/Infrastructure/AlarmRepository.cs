/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Infrastructure/AlarmRepository.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Domain;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NpgsqlTypes;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Alarm.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmRepository
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private readonly NpgsqlDataSource dataSource;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public AlarmRepository(NpgsqlDataSource dataSource)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        this.dataSource = dataSource;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmChanges> EvaluateAsync(AlarmEvaluationRequest request, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var changes = await EvaluateAsync(connection, transaction, request, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return changes;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmChanges> EvaluateAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        AlarmEvaluationRequest request,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var alarmingTagIds = new List<Guid>();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var severities = new List<string>();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var messages = new List<string>();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var clearingTagIds = new List<Guid>();

// LEARN: loops over each item in a collection.
        foreach (var sample in request.Samples)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var result = AlarmRule.Evaluate(sample);
// LEARN: branches only when the boolean condition is true.
            if (result.IsAlarm)
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                alarmingTagIds.Add(sample.TagId);
// LEARN: executes one C# statement; semicolons terminate most statements.
                severities.Add(result.Severity);
// LEARN: executes one C# statement; semicolons terminate most statements.
                messages.Add(result.Message);
            }
// LEARN: runs when the preceding if/else-if conditions did not match.
            else
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                clearingTagIds.Add(sample.TagId);
            }
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var raised = new List<AlarmDto>();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var cleared = new List<AlarmDto>();
// LEARN: branches only when the boolean condition is true.
        if (alarmingTagIds.Count == 0 && clearingTagIds.Count == 0)
        {
// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
            command.Parameters.AddWithValue("tenant_id", request.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
            command.Parameters.AddWithValue("unit_id", request.UnitId);
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
            command.Parameters.Add(new NpgsqlParameter("tag_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = alarmingTagIds.ToArray() });
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
            command.Parameters.Add(new NpgsqlParameter("severities", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = severities.ToArray() });
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
            command.Parameters.Add(new NpgsqlParameter("messages", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = messages.ToArray() });
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
            cleared.AddRange(await ReadAlarmsAsync(command, cancellationToken));

        }

// LEARN: returns a value or exits the current method.
        return new AlarmChanges(raised, cleared);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> RaiseCommunicationLostAsync(UnitDto unit, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var alarm = await RaiseCommunicationLostAsync(connection, transaction, unit, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return alarm;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> RaiseCommunicationLostAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        UnitDto unit,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", unit.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", unit.Id);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("message", $"{unit.Name} communication lost");
// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("alarm_id", alarmId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: returns a value or exits the current method.
        return await ReadAlarmAsync(command, cancellationToken);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> AcknowledgeAsync(Guid alarmId, CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var alarm = await AcknowledgeAsync(connection, transaction, alarmId, user, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return alarm;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<AlarmDto?> AcknowledgeAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        Guid alarmId,
// LEARN: continues an argument/object/collection initializer onto the next line.
        CurrentUserDto user,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update alarm_events
            set state = 'acknowledged', acknowledged_at_utc = now(), acknowledged_by_user_id = @user_id
            where id = @alarm_id and state = 'active' and (@is_support or tenant_id = @tenant_id)
            returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
            """, connection, transaction);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("alarm_id", alarmId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("user_id", user.UserId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: returns a value or exits the current method.
        return await ReadAlarmAsync(command, cancellationToken);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<int> CountForUnitPeriodAsync(Guid unitId, string period, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", unitId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("period_start", range.StartUtc);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("period_end", range.EndUtc);
// LEARN: returns a value or exits the current method.
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task EnqueueOutboxAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        IEnumerable<object> events,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var messages = events.Select(AlarmOutboxEvents.Serialize).ToArray();
// LEARN: branches only when the boolean condition is true.
        if (messages.Length == 0)
        {
// LEARN: returns a value or exits the current method.
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
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Value = messages.Select(message => message.EventType).ToArray()
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        command.Parameters.Add(new NpgsqlParameter("payloads", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Value = messages.Select(message => message.Payload).ToArray()
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<IReadOnlyCollection<AlarmDto>> ReadAlarmsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var results = new List<AlarmDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            results.Add(new AlarmDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(0),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(1),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(2),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(4),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(5),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(6),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetFieldValue<DateTimeOffset>(7),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
// LEARN: executes one C# statement; semicolons terminate most statements.
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
        }

// LEARN: returns a value or exits the current method.
        return results;
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static async Task<AlarmDto?> ReadAlarmAsync(NpgsqlCommand command, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        (await ReadAlarmsAsync(command, cancellationToken)).FirstOrDefault();
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmChanges(IReadOnlyCollection<AlarmDto> Raised, IReadOnlyCollection<AlarmDto> Cleared);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmRouteKeys(Guid TenantId, Guid UnitId, string TenantKey, string SiteKey, string UnitKey);
