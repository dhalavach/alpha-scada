namespace Alpha.Scada.Alarm.Contracts;

public sealed record AlarmCleared(
    Guid AlarmId,
    Guid TenantId,
    Guid UnitId,
    Guid? TagId,
    string TenantKey,
    string SiteKey,
    string UnitKey,
    DateTimeOffset ClearedAtUtc)
{
    public const string SchemaVersion = "1.0";
}
