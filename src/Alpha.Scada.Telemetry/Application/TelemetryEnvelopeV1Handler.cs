using Alpha.Scada.Contracts;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using Wolverine;

namespace Alpha.Scada.Telemetry.Application;

public sealed class TelemetryEnvelopeV1Handler(
    CatalogCache catalog,
    TelemetryRepository repository,
    ILogger<TelemetryEnvelopeV1Handler> logger)
{
    public async Task<TelemetryBatchStored?> Handle(
        TelemetryEnvelopeV1 envelope,
        Envelope envelopeMetadata,
        CancellationToken cancellationToken)
    {
        EnsureSupportedSchema(envelope.PayloadSchemaVersion);

        var subject = ResolveSubject(envelopeMetadata);
        var topic = TelemetryTopicParser.Parse(subject)
            ?? throw new InvalidTelemetryEnvelopeException($"Telemetry arrived on invalid subject '{subject}'.");
        if (!string.Equals(envelope.UnitKey, topic.UnitKey, StringComparison.Ordinal))
        {
            throw new InvalidTelemetryEnvelopeException(
                $"Telemetry envelope unit '{envelope.UnitKey}' did not match subject unit '{topic.UnitKey}'.");
        }

        var batch = await catalog.ResolveAsync(topic, envelope, cancellationToken);
        if (batch is null)
        {
            logger.LogWarning("Telemetry envelope contained no resolvable samples for {Subject}.", subject);
            return null;
        }

        await repository.IngestAsync(new TelemetryIngestRequest(batch.TenantId, batch.UnitId, batch.Samples), cancellationToken);
        return new TelemetryBatchStored(
            batch.TenantId,
            batch.UnitId,
            batch.TenantKey,
            batch.SiteKey,
            batch.UnitKey,
            DateTimeOffset.UtcNow,
            batch.Samples.Select(sample => new StoredSample(
                sample.TagId,
                sample.TagKey,
                sample.Value,
                sample.Quality,
                sample.SourceTimestampUtc)).ToArray());
    }

    private static string ResolveSubject(Envelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope.TopicName))
        {
            return envelope.TopicName;
        }

        if (envelope.Headers.TryGetValue(RawTelemetryHeaders.Subject, out var subject)
            && !string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        if (envelope.Source is not null)
        {
            return envelope.Source.ToString();
        }

        throw new InvalidTelemetryEnvelopeException("Telemetry envelope did not include the source NATS subject.");
    }

    private void EnsureSupportedSchema(string schemaVersion)
    {
        var parts = schemaVersion.Split('.', 2);
        if (parts.Length == 0 || parts[0] != "1")
        {
            throw new InvalidTelemetryEnvelopeException($"Unsupported telemetry schema version '{schemaVersion}'.");
        }

        if (schemaVersion != TelemetryEnvelopeV1.SchemaVersion)
        {
            logger.LogWarning("Processing newer compatible telemetry schema version {SchemaVersion}.", schemaVersion);
        }
    }
}
