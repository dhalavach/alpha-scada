/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/AlarmOutboxDispatcher.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is a hosted background process. ASP.NET Core starts it with the service and stops it with the host cancellation token.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: BackgroundService is the .NET hosted-worker base class; ExecuteAsync runs until the host cancellation token is signaled. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Threading.Channels;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.ServiceDefaults.Messaging;
using Npgsql;
using Wolverine;

namespace Alpha.Scada.Alarm.Application;

// LEARN: declares an interface, a contract that implementations agree to satisfy.
public interface IAlarmOutboxSignal
{
    void Kick();
}

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmOutboxDispatcher(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource,
    IMessageBus bus,
    IConfiguration configuration,
// LEARN: inherits from BackgroundService, the ASP.NET Core base type for long-running hosted workers.
    ILogger<AlarmOutboxDispatcher> logger) : BackgroundService, IAlarmOutboxSignal
{
    private readonly Channel<bool> wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private int BatchSize => Math.Max(1, configuration.GetValue("AlarmOutbox:BatchSize", 25));
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private int MaxAttempts => Math.Max(1, configuration.GetValue("AlarmOutbox:MaxAttempts", 5));
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private TimeSpan SweepInterval => TimeSpan.FromMilliseconds(
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
        var dispatched = 0;
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var rows = await LoadPendingAsync(connection, transaction, cancellationToken);

// LEARN: loops over each item in a collection.
        foreach (var row in rows)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
                var message = AlarmOutboxEvents.Deserialize(row.EventType, row.Payload);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await PublishAsync(message, row.Id, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await MarkDispatchedAsync(connection, transaction, row.Id, cancellationToken);
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
        return dispatched;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task WaitForNextSweepAsync(CancellationToken cancellationToken)
    {
        var wakeup = wakeups.Reader.WaitToReadAsync(cancellationToken).AsTask();
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
        command.Parameters.AddWithValue("max_attempts", MaxAttempts);
        command.Parameters.AddWithValue("batch_size", BatchSize);

        var rows = new List<AlarmOutboxRow>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlarmOutboxRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return rows;
    }

    private ValueTask PublishAsync(object message, Guid outboxId, CancellationToken cancellationToken)
    {
        var deduplicationId = outboxId.ToString("D");
        var options = new DeliveryOptions
        {
            DeduplicationId = deduplicationId
        }.WithHeader(RawTelemetryHeaders.NatsMessageId, deduplicationId);

        return message switch
        {
            AlarmRaised raised => bus.PublishAsync(raised, options),
            AlarmCleared cleared => bus.PublishAsync(cleared, options),
            AlarmAcknowledged acknowledged => bus.PublishAsync(acknowledged, options),
            _ => throw new InvalidOperationException($"Unsupported alarm outbox message type '{message.GetType().Name}'.")
        };
    }

    private static async Task MarkDispatchedAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set dispatched_at_utc = now(), last_error = null
            where id = @id
            """, connection, transaction);
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
        AlarmOutboxRow row,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var attempts = row.Attempts + 1;
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set attempts = @attempts, last_error = @last_error
            where id = @id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", row.Id);
        command.Parameters.AddWithValue("attempts", attempts);
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

    private sealed record AlarmOutboxRow(Guid Id, string EventType, string Payload, int Attempts);
}
