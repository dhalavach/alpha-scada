/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/CanonicalTelemetryHandler.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;

namespace Alpha.Scada.Telemetry.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class CanonicalTelemetryHandler(
    CatalogCache catalog,
    TelemetryRepository repository,
    ILogger<CanonicalTelemetryHandler> logger)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<TelemetryBatchStored?> Handle(CanonicalTelemetry telemetry, CancellationToken cancellationToken)
    {
        var batch = await catalog.ResolveAsync(telemetry, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (batch is null)
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogWarning(
                "Telemetry envelope contained no resolvable samples for {TenantKey}/{SiteKey}/{UnitKey}.",
                telemetry.TenantKey,
                telemetry.SiteKey,
                telemetry.UnitKey);
            return null;
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
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
