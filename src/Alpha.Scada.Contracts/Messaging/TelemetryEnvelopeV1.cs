/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Contracts/Messaging/TelemetryEnvelopeV1.cs
- Module role: Alpha.Scada.Contracts is the shared DTO contract package. These types describe REST payloads and edge wire formats that cross service or process boundaries.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json.Serialization;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Contracts.Messaging;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetryEnvelopeV1(
// LEARN: continues an argument/object/collection initializer onto the next line.
    [property: JsonPropertyName("schemaVersion")] string PayloadSchemaVersion,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    DateTimeOffset TimestampUtc,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    IReadOnlyCollection<TelemetrySampleV1> Samples)
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string SchemaVersion = "1.0";
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetrySampleV1(
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
    DateTimeOffset SourceTimestampUtc);
