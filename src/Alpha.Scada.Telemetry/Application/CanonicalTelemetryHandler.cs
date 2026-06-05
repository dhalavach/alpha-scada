/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/CanonicalTelemetryHandler.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Application.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Infrastructure;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class CanonicalTelemetryHandler(
// LEARN: continues an argument/object/collection initializer onto the next line.
    CatalogCache catalog,
// LEARN: continues an argument/object/collection initializer onto the next line.
    TelemetryRepository repository,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    ILogger<CanonicalTelemetryHandler> logger)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<TelemetryBatchStored?> Handle(CanonicalTelemetry telemetry, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var batch = await catalog.ResolveAsync(telemetry, cancellationToken);
// LEARN: branches only when the boolean condition is true.
        if (batch is null)
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogWarning(
// LEARN: continues an argument/object/collection initializer onto the next line.
                "Telemetry envelope contained no resolvable samples for {TenantKey}/{SiteKey}/{UnitKey}.",
// LEARN: continues an argument/object/collection initializer onto the next line.
                telemetry.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                telemetry.SiteKey,
// LEARN: executes one C# statement; semicolons terminate most statements.
                telemetry.UnitKey);
// LEARN: returns a value or exits the current method.
            return null;
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await repository.IngestAsync(new TelemetryIngestRequest(batch.TenantId, batch.UnitId, batch.Samples), cancellationToken);
// LEARN: returns a value or exits the current method.
        return new TelemetryBatchStored(
// LEARN: continues an argument/object/collection initializer onto the next line.
            batch.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            batch.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            batch.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            batch.SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            batch.UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            DateTimeOffset.UtcNow,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            batch.Samples.Select(sample => new StoredSample(
// LEARN: continues an argument/object/collection initializer onto the next line.
                sample.TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                sample.TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                sample.Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
                sample.Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
                sample.SourceTimestampUtc)).ToArray());
    }
}
