using System.Threading.Channels;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.ServiceDefaults.Messaging;
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
    AlarmOutboxMetrics metrics,
    ILogger<AlarmOutboxDispatcher> logger) : BackgroundService, IAlarmOutboxSignal
{
    private readonly Channel<bool> wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    private int BatchSize => Math.Max(1, configuration.GetValue("AlarmOutbox:BatchSize", 25));
    private int MaxAttempts => Math.Max(1, configuration.GetValue("AlarmOutbox:MaxAttempts", 5));
    private int RetentionDays => Math.Max(1, configuration.GetValue("AlarmOutbox:RetentionDays", 7));
    private TimeSpan ClaimTimeout => TimeSpan.FromSeconds(
        Math.Max(10, configuration.GetValue("AlarmOutbox:ClaimTimeoutSeconds", 120)));
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
        var rows = await ClaimPendingAsync(cancellationToken);

        foreach (var row in rows)
        {
            using var activity = metrics.StartDispatch(row.Id, row.EventType);
            try
            {
                var message = AlarmOutboxEvents.Deserialize(row.EventType, row.Payload);
                await PublishAsync(message, row.Id, cancellationToken);
                await MarkDispatchedAsync(row.Id, cancellationToken);
                dispatched++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                await MarkFailedAsync(row, ex, cancellationToken);
            }
        }

        await PruneDispatchedAsync(cancellationToken);
        await RefreshMetricsAsync(cancellationToken);
        return dispatched;
    }

    private async Task<IReadOnlyCollection<AlarmOutboxRow>> ClaimPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var rows = await ClaimPendingAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
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

    private async Task<IReadOnlyCollection<AlarmOutboxRow>> ClaimPendingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            with candidates as (
                select id
                from alarm_outbox
                where dispatched_at_utc is null
                  and attempts < @max_attempts
                  and (claimed_at_utc is null or claimed_at_utc < now() - @claim_timeout)
                order by occurred_at_utc
                limit @batch_size
                for update skip locked
            )
            update alarm_outbox outbox
            set claimed_at_utc = now()
            from candidates
            where outbox.id = candidates.id
            returning outbox.id, outbox.event_type, outbox.payload::text, outbox.attempts
            """, connection, transaction);
        command.Parameters.AddWithValue("max_attempts", MaxAttempts);
        command.Parameters.AddWithValue("batch_size", BatchSize);
        command.Parameters.AddWithValue("claim_timeout", ClaimTimeout);

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

    private async Task MarkDispatchedAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set dispatched_at_utc = now(), claimed_at_utc = null, last_error = null
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        AlarmOutboxRow row,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var attempts = row.Attempts + 1;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set attempts = @attempts, claimed_at_utc = null, last_error = @last_error
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", row.Id);
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.AddWithValue("last_error", Truncate(exception.Message, 2_000));
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

    private async Task PruneDispatchedAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            delete from alarm_outbox
            where dispatched_at_utc < now() - @retention
            """, connection);
        command.Parameters.AddWithValue("retention", TimeSpan.FromDays(RetentionDays));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RefreshMetricsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select
                count(*) filter (where dispatched_at_utc is null),
                count(*) filter (where dispatched_at_utc is null and attempts >= @max_attempts)
            from alarm_outbox
            """, connection);
        command.Parameters.AddWithValue("max_attempts", MaxAttempts);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        metrics.Update(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record AlarmOutboxRow(Guid Id, string EventType, string Payload, int Attempts);
}
