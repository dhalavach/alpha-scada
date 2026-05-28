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
