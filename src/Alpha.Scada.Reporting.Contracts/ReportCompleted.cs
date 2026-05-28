namespace Alpha.Scada.Reporting.Contracts;

public sealed record ReportCompleted(
    Guid RequestId,
    Guid ReportId,
    Guid TenantId,
    Guid UnitId,
    string Period,
    DateTimeOffset CompletedAtUtc,
    Guid? CorrelationId)
{
    public const string SchemaVersion = "1.0";
}
