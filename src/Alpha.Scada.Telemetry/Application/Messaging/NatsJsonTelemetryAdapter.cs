/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/Messaging/NatsJsonTelemetryAdapter.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file sits in the application layer, where use cases coordinate domain concepts and infrastructure ports.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Text.Json;
using Alpha.Scada.Contracts.Messaging;

namespace Alpha.Scada.Telemetry.Application.Messaging;

public sealed class NatsJsonTelemetryAdapter : ITelemetryAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool CanHandle(TelemetrySource source) =>
        TelemetryTopicParser.Parse(source.Subject) is not null;

    public CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source)
    {
        var topic = TelemetryTopicParser.Parse(source.Subject)
            ?? throw new InvalidTelemetryEnvelopeException($"Telemetry arrived on invalid subject '{source.Subject}'.");

        var envelope = JsonSerializer.Deserialize<TelemetryEnvelopeV1>(payload.Span, JsonOptions)
            ?? throw new InvalidTelemetryEnvelopeException("Telemetry payload was empty.");

        EnsureSupportedSchema(envelope.PayloadSchemaVersion);
        if (!string.Equals(envelope.UnitKey, topic.UnitKey, StringComparison.Ordinal))
        {
            throw new InvalidTelemetryEnvelopeException(
                $"Telemetry envelope unit '{envelope.UnitKey}' did not match subject unit '{topic.UnitKey}'.");
        }

        return new CanonicalTelemetry(
            topic.TenantKey,
            topic.SiteKey,
            topic.UnitKey,
            envelope.TimestampUtc,
            envelope.Samples.Select(sample => new CanonicalReading(
                sample.TagKey,
                sample.Value,
                sample.Quality,
                sample.SourceTimestampUtc)).ToArray());
    }

    private static void EnsureSupportedSchema(string schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            throw new InvalidTelemetryEnvelopeException("Telemetry payload did not specify a schema version.");
        }

        var parts = schemaVersion.Split('.', 2);
        if (parts.Length == 0 || parts[0] != "1")
        {
            throw new InvalidTelemetryEnvelopeException($"Unsupported telemetry schema version '{schemaVersion}'.");
        }
    }
}
