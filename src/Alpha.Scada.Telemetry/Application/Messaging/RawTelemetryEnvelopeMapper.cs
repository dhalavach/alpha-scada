using System.Security.Cryptography;
using System.Text.Json;
using Alpha.Scada.Contracts.Messaging;
using MQTTnet;
using Wolverine;
using Wolverine.MQTT;

namespace Alpha.Scada.Telemetry.Application.Messaging;

public sealed class RawTelemetryEnvelopeMapper : IMqttEnvelopeMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void MapIncomingToEnvelope(Envelope envelope, MqttApplicationMessage incoming)
    {
        var payloadSegment = incoming.PayloadSegment;
        var payload = payloadSegment.Array is null
            ? []
            : payloadSegment.Array.AsSpan(payloadSegment.Offset, payloadSegment.Count).ToArray();

        envelope.Data = payload;
        envelope.Message = JsonSerializer.Deserialize<TelemetryEnvelopeV1>(payload, JsonOptions)
            ?? throw new JsonException("Telemetry MQTT payload was empty.");
        envelope.MessageType = typeof(TelemetryEnvelopeV1).FullName;
        envelope.ContentType = "application/json";
        envelope.TopicName = incoming.Topic;
        envelope.Id = DeterministicMessageId(incoming.Topic, payload);
        envelope.DeduplicationId = envelope.Id.ToString("N");
    }

    public void MapEnvelopeToOutgoing(Envelope envelope, MqttApplicationMessage outgoing)
    {
        if (envelope.Data is { Length: > 0 } data)
        {
            outgoing.PayloadSegment = new ArraySegment<byte>(data);
        }
    }

    private static Guid DeterministicMessageId(string topic, byte[] payload)
    {
        var topicBytes = JsonSerializer.SerializeToUtf8Bytes(topic, JsonOptions);
        Span<byte> hash = stackalloc byte[32];
        using var sha256 = SHA256.Create();
        sha256.TryComputeHash([.. topicBytes, .. payload], hash, out _);
        return new Guid(hash[..16]);
    }
}
