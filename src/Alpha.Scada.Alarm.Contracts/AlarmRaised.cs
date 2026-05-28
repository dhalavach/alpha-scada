namespace Alpha.Scada.Alarm.Contracts;

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
