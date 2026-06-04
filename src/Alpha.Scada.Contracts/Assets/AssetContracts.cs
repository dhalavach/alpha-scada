namespace Alpha.Scada.Contracts;

public sealed record SiteDto(Guid Id, Guid TenantId, string Key, string Name, string Region, string Status);

public sealed record UnitDto(
    Guid Id,
    Guid TenantId,
    Guid SiteId,
    string Key,
    string Name,
    string Model,
    string Status,
    DateTimeOffset? LastSeenUtc);

public sealed record ResolvedUnitDto(Guid TenantId, Guid SiteId, Guid UnitId, string UnitName, string UnitStatus);

public sealed record UnitRouteDto(Guid TenantId, Guid SiteId, Guid UnitId, string SiteKey, string UnitKey, string UnitName);
