/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry.Contracts/TelemetryBatchStored.cs
- Module role: Alpha.Scada.Telemetry.Contracts is the telemetry domain-event contract package. It contains normalized events emitted after Telemetry has resolved and persisted raw edge data.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry.Contracts;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetryBatchStored(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    DateTimeOffset StoredAtUtc,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    IReadOnlyList<StoredSample> Samples)
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string SchemaVersion = "1.0";
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record StoredSample(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
    DateTimeOffset SourceTimestampUtc);
