/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/Messaging/TelemetryAdapter.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Telemetry.Application.Messaging;

// LEARN: declares an interface, a contract that implementations agree to satisfy.
public interface ITelemetryAdapter
{
    bool CanHandle(TelemetrySource source);

    CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source);
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetrySource(string Subject, IReadOnlyDictionary<string, string?> Headers);

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryAdapterResolver(IEnumerable<ITelemetryAdapter> adapters)
{
    private readonly IReadOnlyCollection<ITelemetryAdapter> adapters = adapters.ToArray();

    public CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source)
    {
        var adapter = adapters.FirstOrDefault(candidate => candidate.CanHandle(source))
            ?? throw new InvalidTelemetryEnvelopeException($"No telemetry adapter can handle subject '{source.Subject}'.");

        return adapter.Normalize(payload, source);
    }
}
