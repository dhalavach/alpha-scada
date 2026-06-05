/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/Messaging/NatsJsonTelemetryAdapter.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file sits in the application layer, where use cases coordinate domain concepts and infrastructure ports.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts.Messaging;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Application.Messaging;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class NatsJsonTelemetryAdapter : ITelemetryAdapter
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public bool CanHandle(TelemetrySource source) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        TelemetryTopicParser.Parse(source.Subject) is not null;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var topic = TelemetryTopicParser.Parse(source.Subject)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidTelemetryEnvelopeException($"Telemetry arrived on invalid subject '{source.Subject}'.");

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var envelope = JsonSerializer.Deserialize<TelemetryEnvelopeV1>(payload.Span, JsonOptions)
// LEARN: creates a new object or record instance.
            ?? throw new InvalidTelemetryEnvelopeException("Telemetry payload was empty.");

// LEARN: executes one C# statement; semicolons terminate most statements.
        EnsureSupportedSchema(envelope.PayloadSchemaVersion);
// LEARN: branches only when the boolean condition is true.
        if (!string.Equals(envelope.UnitKey, topic.UnitKey, StringComparison.Ordinal))
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidTelemetryEnvelopeException(
// LEARN: executes one C# statement; semicolons terminate most statements.
                $"Telemetry envelope unit '{envelope.UnitKey}' did not match subject unit '{topic.UnitKey}'.");
        }

// LEARN: returns a value or exits the current method.
        return new CanonicalTelemetry(
// LEARN: continues an argument/object/collection initializer onto the next line.
            topic.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            topic.SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            topic.UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            envelope.TimestampUtc,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            envelope.Samples.Select(sample => new CanonicalReading(
// LEARN: continues an argument/object/collection initializer onto the next line.
                sample.TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                sample.Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
                sample.Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
                sample.SourceTimestampUtc)).ToArray());
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static void EnsureSupportedSchema(string schemaVersion)
    {
// LEARN: branches only when the boolean condition is true.
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidTelemetryEnvelopeException("Telemetry payload did not specify a schema version.");
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var parts = schemaVersion.Split('.', 2);
// LEARN: branches only when the boolean condition is true.
        if (parts.Length == 0 || parts[0] != "1")
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidTelemetryEnvelopeException($"Unsupported telemetry schema version '{schemaVersion}'.");
        }
    }
}
