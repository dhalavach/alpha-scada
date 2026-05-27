namespace Alpha.Scada.Asset.Domain;

public sealed record Site(Guid Id, Guid TenantId, string Key, string Name, string Region, string Status);

public sealed record Unit(Guid Id, Guid TenantId, Guid SiteId, string Key, string Name, string Model, string Status, DateTimeOffset? LastSeenUtc);
