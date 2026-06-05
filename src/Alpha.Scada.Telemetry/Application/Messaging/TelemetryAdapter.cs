/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/Messaging/TelemetryAdapter.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Application.Messaging;

// LEARN: declares an interface, a contract that implementations agree to satisfy.
public interface ITelemetryAdapter
{
// LEARN: executes one C# statement; semicolons terminate most statements.
    bool CanHandle(TelemetrySource source);

// LEARN: executes one C# statement; semicolons terminate most statements.
    CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source);
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetrySource(string Subject, IReadOnlyDictionary<string, string?> Headers);

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryAdapterResolver(IEnumerable<ITelemetryAdapter> adapters)
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private readonly IReadOnlyCollection<ITelemetryAdapter> adapters = adapters.ToArray();

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var adapter = adapters.FirstOrDefault(candidate => candidate.CanHandle(source))
// LEARN: creates a new object or record instance.
            ?? throw new InvalidTelemetryEnvelopeException($"No telemetry adapter can handle subject '{source.Subject}'.");

// LEARN: returns a value or exits the current method.
        return adapter.Normalize(payload, source);
    }
}
