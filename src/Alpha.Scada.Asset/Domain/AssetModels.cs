/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Domain/AssetModels.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: domain files should stay independent of ASP.NET, NATS, Wolverine, and SQL so business rules can be reasoned about directly.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Asset.Domain;

public sealed record Site(Guid Id, Guid TenantId, string Key, string Name, string Region, string Status);

public sealed record Unit(Guid Id, Guid TenantId, Guid SiteId, string Key, string Name, string Model, string Status, DateTimeOffset? LastSeenUtc);
