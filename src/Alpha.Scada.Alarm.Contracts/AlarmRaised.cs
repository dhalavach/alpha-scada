/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm.Contracts/AlarmRaised.cs
- Module role: Alpha.Scada.Alarm.Contracts is the alarm domain-event contract package. It contains alarm lifecycle messages that other services consume without depending on Alarm internals.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Alarm.Contracts;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmRaised(
    Guid AlarmId,
    Guid TenantId,
    Guid UnitId,
    Guid? TagId,
    string TenantKey,
    string SiteKey,
    string UnitKey,
    string Severity,
    string Message,
    DateTimeOffset RaisedAtUtc)
{
    public const string SchemaVersion = "1.0";
}
