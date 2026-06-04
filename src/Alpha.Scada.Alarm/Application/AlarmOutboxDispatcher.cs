using System.Threading.Channels;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Npgsql;
using Wolverine;

namespace Alpha.Scada.Alarm.Application;

public interface IAlarmOutboxSignal
{
    void Kick();
}

public sealed class AlarmOutboxDispatcher(
    NpgsqlDataSource dataSource,
    IMessageBus bus,
    IConfiguration configuration,
    ILogger<AlarmOutboxDispatcher> logger) : BackgroundService, IAlarmOutboxSignal
{
    private readonly Channel<bool> wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    private int BatchSize => Math.Max(1, configuration.GetValue("AlarmOutbox:BatchSize", 25));
    private int MaxAttempts => Math.Max(1, configuration.GetValue("AlarmOutbox:MaxAttempts", 5));
    private TimeSpan SweepInterval => TimeSpan.FromMilliseconds(
        Math.Max(100, configuration.GetValue("AlarmOutbox:SweepIntervalMilliseconds", 1_000)));

    public void Kick() => wakeups.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Alarm outbox dispatch sweep failed.");
            }

            await WaitForNextSweepAsync(stoppingToken);
        }
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var dispatched = 0;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var rows = await LoadPendingAsync(connection, transaction, cancellationToken);

        foreach (var row in rows)
        {
            try
            {
                var message = AlarmOutboxEvents.Deserialize(row.EventType, row.Payload);
                await PublishAsync(message, row.Id, cancellationToken);
                await MarkDispatchedAsync(connection, transaction, row.Id, cancellationToken);
                dispatched++;
            }
            catch (Exception ex)
            {
                await MarkFailedAsync(connection, transaction, row, ex, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return dispatched;
    }

    private async Task WaitForNextSweepAsync(CancellationToken cancellationToken)
    {
        var wakeup = wakeups.Reader.WaitToReadAsync(cancellationToken).AsTask();
        var delay = Task.Delay(SweepInterval, cancellationToken);
        await Task.WhenAny(wakeup, delay);
        while (wakeups.Reader.TryRead(out _))
        {
        }
    }

    private async Task<IReadOnlyCollection<AlarmOutboxRow>> LoadPendingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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
        var options = new DeliveryOptions
        {
            DeduplicationId = outboxId.ToString("D")
        };

        return message switch
        {
            AlarmRaised raised => bus.PublishAsync(raised, options),
            AlarmCleared cleared => bus.PublishAsync(cleared, options),
            AlarmAcknowledged acknowledged => bus.PublishAsync(acknowledged, options),
            _ => throw new InvalidOperationException($"Unsupported alarm outbox message type '{message.GetType().Name}'.")
        };
    }

    private static async Task MarkDispatchedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set dispatched_at_utc = now(), last_error = null
            where id = @id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AlarmOutboxRow row,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var attempts = row.Attempts + 1;
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set attempts = @attempts, last_error = @last_error
            where id = @id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", row.Id);
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.AddWithValue("last_error", exception.Message);
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (attempts >= MaxAttempts)
        {
            logger.LogError(exception, "Alarm outbox row {OutboxId} failed after {Attempts} attempts and will not be retried.", row.Id, attempts);
        }
        else
        {
            logger.LogWarning(exception, "Alarm outbox row {OutboxId} failed on attempt {Attempts}.", row.Id, attempts);
        }
    }

    private sealed record AlarmOutboxRow(Guid Id, string EventType, string Payload, int Attempts);
}
