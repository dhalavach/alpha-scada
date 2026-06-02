using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using Wolverine;

namespace Alpha.Scada.Telemetry.Application;

public sealed class TelemetryIngestionHandler(
    CatalogCache catalog,
    TelemetryRepository repository,
    ILogger<TelemetryIngestionHandler> logger)
{
    public async Task Handle(TelemetryEnvelopeV1 envelope, Envelope messageEnvelope, CancellationToken cancellationToken)
    {
        EnsureSupportedSchema(envelope.PayloadSchemaVersion);

        var topic = TelemetryTopicParser.Parse(messageEnvelope.TopicName);
        if (topic is null)
        {
            throw new InvalidOperationException($"Telemetry arrived on invalid topic '{messageEnvelope.TopicName}'.");
        }

        var batch = await catalog.ResolveAsync(topic, envelope, cancellationToken);
        if (batch is null)
        {
            logger.LogWarning("Telemetry envelope contained no resolvable samples for {Topic}.", messageEnvelope.TopicName);
            return;
        }

        var stored = new TelemetryBatchStored(
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

        await repository.IngestAsync(
            new(batch.TenantId, batch.UnitId, batch.Samples),
            cancellationToken,
            [stored]);
    }

    private void EnsureSupportedSchema(string schemaVersion)
    {
        var parts = schemaVersion.Split('.', 2);
        if (parts.Length == 0 || parts[0] != "1")
        {
            throw new InvalidOperationException($"Unsupported telemetry schema version '{schemaVersion}'.");
        }

        if (schemaVersion != TelemetryEnvelopeV1.SchemaVersion)
        {
            logger.LogWarning("Processing newer compatible telemetry schema version {SchemaVersion}.", schemaVersion);
        }
    }
}
