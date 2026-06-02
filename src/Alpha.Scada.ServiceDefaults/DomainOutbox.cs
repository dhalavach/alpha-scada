using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using Wolverine;

namespace Alpha.Scada.ServiceDefaults;

public sealed class DomainOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists domain_outbox_messages (
                id uuid primary key,
                message_type text not null,
                payload jsonb not null,
                created_at_utc timestamptz not null default now(),
                locked_until_utc timestamptz,
                dispatched_at_utc timestamptz,
                attempts integer not null default 0,
                last_error text
            );

            create index if not exists ix_domain_outbox_pending
                on domain_outbox_messages(created_at_utc)
                where dispatched_at_utc is null;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        object message,
        CancellationToken cancellationToken)
    {
        var messageType = message.GetType().AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Message type {message.GetType().FullName} cannot be resolved.");
        var payload = JsonSerializer.Serialize(message, message.GetType(), JsonOptions);

        await using var command = new NpgsqlCommand("""
            insert into domain_outbox_messages (id, message_type, payload)
            values (@id, @message_type, @payload::jsonb)
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("message_type", messageType);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Text) { Value = payload });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static object Deserialize(string messageType, string payload)
    {
        var type = Type.GetType(messageType, throwOnError: false)
            ?? throw new InvalidOperationException($"Outbox message type {messageType} cannot be loaded.");
        return JsonSerializer.Deserialize(payload, type, JsonOptions)
            ?? throw new InvalidOperationException($"Outbox payload for {messageType} was empty.");
    }
}

public sealed class DomainOutboxDispatcher(
    NpgsqlDataSource dataSource,
    IMessageBus bus,
    IConfiguration configuration,
    ILogger<DomainOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("DomainOutbox:Enabled", true))
        {
            logger.LogInformation("Domain outbox dispatcher is disabled.");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(configuration.GetValue("DomainOutbox:PollMilliseconds", 500));
        var batchSize = Math.Clamp(configuration.GetValue("DomainOutbox:BatchSize", 25), 1, 250);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await LockNextBatchAsync(batchSize, stoppingToken);
                foreach (var message in messages)
                {
                    await DispatchAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Domain outbox dispatch cycle failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task<IReadOnlyCollection<OutboxMessage>> LockNextBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await DomainOutbox.EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            with next_messages as (
                select id
                from domain_outbox_messages
                where dispatched_at_utc is null
                  and (locked_until_utc is null or locked_until_utc < now())
                order by created_at_utc
                limit @batch_size
                for update skip locked
            )
            update domain_outbox_messages message
            set locked_until_utc = now() + interval '30 seconds',
                attempts = attempts + 1
            from next_messages
            where message.id = next_messages.id
            returning message.id, message.message_type, message.payload::text
            """, connection, transaction);
        command.Parameters.AddWithValue("batch_size", batchSize);

        var messages = new List<OutboxMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxMessage(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await bus.PublishAsync(DomainOutbox.Deserialize(message.MessageType, message.Payload));
            await MarkDispatchedAsync(message.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Domain outbox message {MessageId} failed to publish.", message.Id);
            await ReleaseAsync(message.Id, ex.Message, cancellationToken);
        }
    }

    private async Task MarkDispatchedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update domain_outbox_messages
            set dispatched_at_utc = now(),
                locked_until_utc = null,
                last_error = null
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ReleaseAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update domain_outbox_messages
            set locked_until_utc = null,
                last_error = @error
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record OutboxMessage(Guid Id, string MessageType, string Payload);
}

public static class DomainOutboxRegistration
{
    public static IServiceCollection AddDomainOutbox(this IServiceCollection services)
    {
        services.AddSingleton<DomainOutbox>();
        services.AddHostedService<DomainOutboxDispatcher>();
        return services;
    }
}
