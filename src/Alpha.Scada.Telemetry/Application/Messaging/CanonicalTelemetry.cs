/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/Messaging/CanonicalTelemetry.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Application.Messaging;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record CanonicalTelemetry(
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    DateTimeOffset OccurredAtUtc,
// LEARN: executes one C# statement; semicolons terminate most statements.
    IReadOnlyCollection<CanonicalReading> Readings);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record CanonicalReading(
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
    DateTimeOffset SourceTimestampUtc);
