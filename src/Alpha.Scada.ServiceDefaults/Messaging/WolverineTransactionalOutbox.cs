using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using Wolverine;
using Wolverine.Runtime;

namespace Alpha.Scada.ServiceDefaults.Messaging;

public sealed class WolverineTransactionalOutbox
{
    private readonly IMessageBus bus;
    private readonly NpgsqlDataSource? dataSource;
    private readonly IWolverineRuntime? runtime;
    private readonly string outgoingTableName;

    public WolverineTransactionalOutbox(
        IMessageBus bus,
        IConfiguration configuration,
        NpgsqlDataSource? dataSource = null,
        IWolverineRuntime? runtime = null)
    {
        this.bus = bus;
        this.dataSource = dataSource;
        this.runtime = runtime;
        var schema = configuration.GetValue("Wolverine:StorageSchema", "wolverine");
        outgoingTableName = $"{ValidateIdentifier(schema)}.wolverine_outgoing_envelopes";
    }

    public async Task<WolverineOutboxBatch> StoreAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        T message,
        CancellationToken cancellationToken)
    {
        var envelopes = bus.PreviewSubscriptions(message!);
        foreach (var envelope in envelopes)
        {
            await StoreEnvelopeAsync(connection, transaction, envelope, cancellationToken);
        }

        return new WolverineOutboxBatch(
            envelopes.Select(envelope => envelope.Id).ToArray(),
            token => bus.PublishAsync(message!).AsTask());
    }

    public async Task PublishAndClearAsync(WolverineOutboxBatch batch, CancellationToken cancellationToken)
    {
        await batch.PublishAsync(cancellationToken);
        await ClearAsync(batch.EnvelopeIds, cancellationToken);
    }

    private async Task ClearAsync(IReadOnlyCollection<Guid> envelopeIds, CancellationToken cancellationToken)
    {
        if (envelopeIds.Count == 0)
        {
            return;
        }

        if (dataSource is null)
        {
            throw new InvalidOperationException("NpgsqlDataSource is required to clear Wolverine outbox fallback rows.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            delete from {outgoingTableName}
            where id = any(@ids)
            """, connection);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = envelopeIds.ToArray()
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task StoreEnvelopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Envelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.Destination is null)
        {
            throw new InvalidOperationException($"Wolverine did not resolve a destination for {envelope.MessageType}.");
        }

        var messageType = envelope.MessageType
            ?? throw new InvalidOperationException("Wolverine did not resolve a message type for an outgoing envelope.");

        await using var command = new NpgsqlCommand($"""
            insert into {outgoingTableName} (id, owner_id, destination, deliver_by, body, attempts, message_type)
            values (@id, @owner_id, @destination, @deliver_by, @body, @attempts, @message_type)
            on conflict (id) do nothing
            """, connection, transaction);
        command.Parameters.AddWithValue("id", envelope.Id);
        command.Parameters.AddWithValue("owner_id", ResolveOwnerId(envelope));
        command.Parameters.AddWithValue("destination", envelope.Destination.ToString());
        command.Parameters.Add(new NpgsqlParameter("deliver_by", NpgsqlDbType.TimestampTz)
        {
            Value = envelope.DeliverBy is null ? DBNull.Value : envelope.DeliverBy.Value
        });
        command.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Bytea)
        {
            Value = await envelope.GetDataAsync()
        });
        command.Parameters.AddWithValue("attempts", envelope.Attempts);
        command.Parameters.AddWithValue("message_type", messageType);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private int ResolveOwnerId(Envelope envelope)
    {
        if (envelope.OwnerId != 0)
        {
            return envelope.OwnerId;
        }

        var nodeController = runtime?.GetType().GetProperty("NodeController")?.GetValue(runtime);
        return ReadIntProperty(nodeController, "AssignedNodeNumber")
            ?? ReadIntProperty(nodeController, "NodeNumber")
            ?? envelope.OwnerId;
    }

    private static int? ReadIntProperty(object? instance, string propertyName)
    {
        var value = instance?.GetType().GetProperty(propertyName)?.GetValue(instance);
        return value is int number && number > 0 ? number : null;
    }

    private static string ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException($"Invalid Wolverine storage schema '{value}'.");
        }

        return value;
    }
}

public sealed record WolverineOutboxBatch(
    IReadOnlyCollection<Guid> EnvelopeIds,
    Func<CancellationToken, Task> PublishAsync);
