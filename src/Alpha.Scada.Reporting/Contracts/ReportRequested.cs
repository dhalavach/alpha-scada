namespace Alpha.Scada.Reporting.Contracts;

public sealed record ReportRequested(
    Guid RequestId,
    Guid TenantId,
    Guid UnitId,
    string Period,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    Guid? CorrelationId)
{
    public const string SchemaVersion = "1.0";
}
