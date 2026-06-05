/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Contracts/Assets/AssetContracts.cs
- Module role: Alpha.Scada.Contracts is the shared DTO contract package. These types describe REST payloads and edge wire formats that cross service or process boundaries.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Contracts;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record SiteDto(Guid Id, Guid TenantId, string Key, string Name, string Region, string Status);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record UnitDto(
    Guid Id,
    Guid TenantId,
    Guid SiteId,
    string Key,
    string Name,
    string Model,
    string Status,
    DateTimeOffset? LastSeenUtc);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ResolvedUnitDto(Guid TenantId, Guid SiteId, Guid UnitId, string UnitName, string UnitStatus);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record UnitRouteDto(Guid TenantId, Guid SiteId, Guid UnitId, string SiteKey, string UnitKey, string UnitName);
