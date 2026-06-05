/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/AlarmOutboxDispatcher.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is a hosted background process. ASP.NET Core starts it with the service and stops it with the host cancellation token.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: BackgroundService is the .NET hosted-worker base class; ExecuteAsync runs until the host cancellation token is signaled. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Threading.Channels;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Alarm.Application;

// LEARN: declares an interface, a contract that implementations agree to satisfy.
public interface IAlarmOutboxSignal
{
// LEARN: executes one C# statement; semicolons terminate most statements.
    void Kick();
}

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmOutboxDispatcher(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource,
// LEARN: continues an argument/object/collection initializer onto the next line.
    IMessageBus bus,
// LEARN: continues an argument/object/collection initializer onto the next line.
    IConfiguration configuration,
// LEARN: inherits from BackgroundService, the ASP.NET Core base type for long-running hosted workers.
    ILogger<AlarmOutboxDispatcher> logger) : BackgroundService, IAlarmOutboxSignal
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private readonly Channel<bool> wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
// LEARN: continues an argument/object/collection initializer onto the next line.
        FullMode = BoundedChannelFullMode.DropWrite,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        SingleReader = true
// LEARN: executes one C# statement; semicolons terminate most statements.
    });

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private int BatchSize => Math.Max(1, configuration.GetValue("AlarmOutbox:BatchSize", 25));
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private int MaxAttempts => Math.Max(1, configuration.GetValue("AlarmOutbox:MaxAttempts", 5));
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private TimeSpan SweepInterval => TimeSpan.FromMilliseconds(
// LEARN: executes one C# statement; semicolons terminate most statements.
        Math.Max(100, configuration.GetValue("AlarmOutbox:SweepIntervalMilliseconds", 1_000)));

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public void Kick() => wakeups.Writer.TryWrite(true);

// LEARN: overrides the hosted-worker entry point; the host calls this when the service starts.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
// LEARN: starts a loop that continues while its condition remains true.
        while (!stoppingToken.IsCancellationRequested)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await DispatchPendingAsync(stoppingToken);
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
// LEARN: throws an exception to signal that this path cannot continue safely.
                throw;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (Exception ex)
            {
// LEARN: writes structured log output; placeholders become searchable log fields.
                logger.LogWarning(ex, "Alarm outbox dispatch sweep failed.");
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForNextSweepAsync(stoppingToken);
        }
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var dispatched = 0;
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var rows = await LoadPendingAsync(connection, transaction, cancellationToken);

// LEARN: loops over each item in a collection.
        foreach (var row in rows)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var message = AlarmOutboxEvents.Deserialize(row.EventType, row.Payload);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await PublishAsync(message, row.Id, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await MarkDispatchedAsync(connection, transaction, row.Id, cancellationToken);
// LEARN: executes one C# statement; semicolons terminate most statements.
                dispatched++;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (Exception ex)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await MarkFailedAsync(connection, transaction, row, ex, cancellationToken);
            }
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return dispatched;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task WaitForNextSweepAsync(CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var wakeup = wakeups.Reader.WaitToReadAsync(cancellationToken).AsTask();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var delay = Task.Delay(SweepInterval, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await Task.WhenAny(wakeup, delay);
// LEARN: starts a loop that continues while its condition remains true.
        while (wakeups.Reader.TryRead(out _))
        {
        }
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task<IReadOnlyCollection<AlarmOutboxRow>> LoadPendingAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, event_type, payload::text, attempts
            from alarm_outbox
            where dispatched_at_utc is null and attempts < @max_attempts
            order by occurred_at_utc
            limit @batch_size
            for update skip locked
            """, connection, transaction);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("max_attempts", MaxAttempts);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("batch_size", BatchSize);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var rows = new List<AlarmOutboxRow>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            rows.Add(new AlarmOutboxRow(
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(0),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(1),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(2),
// LEARN: executes one C# statement; semicolons terminate most statements.
                reader.GetInt32(3)));
        }

// LEARN: returns a value or exits the current method.
        return rows;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private ValueTask PublishAsync(object message, Guid outboxId, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var deduplicationId = outboxId.ToString("D");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var options = new DeliveryOptions
        {
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            DeduplicationId = deduplicationId
// LEARN: executes one C# statement; semicolons terminate most statements.
        }.WithHeader(RawTelemetryHeaders.NatsMessageId, deduplicationId);

// LEARN: returns a value or exits the current method.
        return message switch
        {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmRaised raised => bus.PublishAsync(raised, options),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmCleared cleared => bus.PublishAsync(cleared, options),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmAcknowledged acknowledged => bus.PublishAsync(acknowledged, options),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            _ => throw new InvalidOperationException($"Unsupported alarm outbox message type '{message.GetType().Name}'.")
        };
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task MarkDispatchedAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        Guid id,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set dispatched_at_utc = now(), last_error = null
            where id = @id
            """, connection, transaction);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("id", id);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task MarkFailedAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        AlarmOutboxRow row,
// LEARN: continues an argument/object/collection initializer onto the next line.
        Exception exception,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var attempts = row.Attempts + 1;
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set attempts = @attempts, last_error = @last_error
            where id = @id
            """, connection, transaction);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("id", row.Id);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("attempts", attempts);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("last_error", exception.Message);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync(cancellationToken);

// LEARN: branches only when the boolean condition is true.
        if (attempts >= MaxAttempts)
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogError(exception, "Alarm outbox row {OutboxId} failed after {Attempts} attempts and will not be retried.", row.Id, attempts);
        }
// LEARN: runs when the preceding if/else-if conditions did not match.
        else
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogWarning(exception, "Alarm outbox row {OutboxId} failed on attempt {Attempts}.", row.Id, attempts);
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private sealed record AlarmOutboxRow(Guid Id, string EventType, string Payload, int Attempts);
}
