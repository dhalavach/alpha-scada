/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Contracts/Alarms/AlarmContracts.cs
- Module role: Alpha.Scada.Contracts is the shared DTO contract package. These types describe REST payloads and edge wire formats that cross service or process boundaries.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Contracts;

public sealed record AlarmDto(
    Guid Id,
    Guid TenantId,
    Guid UnitId,
    Guid? TagId,
    string Severity,
    string Message,
    string State,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? ClearedAtUtc);

public sealed record AlarmEvaluationRequest(
    Guid TenantId,
    Guid UnitId,
    string UnitName,
    IReadOnlyCollection<ResolvedTelemetrySample> Samples);
