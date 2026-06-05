/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting.Contracts/ReportCompleted.cs
- Module role: Alpha.Scada.Reporting.Contracts is the reporting job/event contract package. It contains report request/completion messages used by Gateway and Reporting.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Reporting.Contracts;

public sealed record ReportCompleted(
    Guid RequestId,
    Guid ReportId,
    Guid TenantId,
    Guid UnitId,
    string Period,
    DateTimeOffset CompletedAtUtc,
    Guid? CorrelationId)
{
    public const string SchemaVersion = "1.0";
}
