/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset.Contracts/UnitStatusChanged.cs
- Module role: Alpha.Scada.Asset.Contracts is the asset domain-event contract package. It contains messages published by Asset and consumed by services such as Gateway and Alarm.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Asset.Contracts;

public sealed record UnitStatusChanged(
    Guid TenantId,
    Guid SiteId,
    Guid UnitId,
    string TenantKey,
    string SiteKey,
    string UnitKey,
    string UnitName,
    string Status,
    DateTimeOffset ChangedAtUtc,
    DateTimeOffset? LastSeenUtc)
{
    public const string SchemaVersion = "1.0";
}
