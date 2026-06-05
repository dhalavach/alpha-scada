/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Contracts/Telemetry/TelemetryContracts.cs
- Module role: Alpha.Scada.Contracts is the shared DTO contract package. These types describe REST payloads and edge wire formats that cross service or process boundaries.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Contracts;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TagCurrentDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Name,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Subsystem,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string EngineeringUnit,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
    DateTimeOffset TimestampUtc);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TagValueDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
    DateTimeOffset TimestampUtc);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetryHistoryPointDto(DateTimeOffset TimestampUtc, double Value, string Quality);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ResolvedTelemetrySample(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string TagName,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Subsystem,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string EngineeringUnit,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double? AlarmLow,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double? AlarmHigh,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
    DateTimeOffset SourceTimestampUtc);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetryIngestRequest(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid UnitId,
// LEARN: executes one C# statement; semicolons terminate most statements.
    IReadOnlyCollection<ResolvedTelemetrySample> Samples);
