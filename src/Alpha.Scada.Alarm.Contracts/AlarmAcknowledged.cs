namespace Alpha.Scada.Alarm.Contracts;

public sealed record AlarmAcknowledged(
    Guid AlarmId,
    Guid TenantId,
    Guid UnitId,
    Guid? TagId,
    string TenantKey,
    string SiteKey,
    string UnitKey,
    Guid AcknowledgedByUserId,
    DateTimeOffset AcknowledgedAtUtc)
{
    public const string SchemaVersion = "1.0";
}
