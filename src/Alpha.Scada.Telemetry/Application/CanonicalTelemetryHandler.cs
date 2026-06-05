using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;

namespace Alpha.Scada.Telemetry.Application;

public sealed class CanonicalTelemetryHandler(
    CatalogCache catalog,
    TelemetryRepository repository,
    ILogger<CanonicalTelemetryHandler> logger)
{
    public async Task<TelemetryBatchStored?> Handle(CanonicalTelemetry telemetry, CancellationToken cancellationToken)
    {
        var batch = await catalog.ResolveAsync(telemetry, cancellationToken);
        if (batch is null)
        {
            logger.LogWarning(
                "Telemetry envelope contained no resolvable samples for {TenantKey}/{SiteKey}/{UnitKey}.",
                telemetry.TenantKey,
                telemetry.SiteKey,
                telemetry.UnitKey);
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
}
