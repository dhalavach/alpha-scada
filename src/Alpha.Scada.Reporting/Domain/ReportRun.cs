namespace Alpha.Scada.Reporting.Domain;

public sealed record ReportRun(Guid Id, Guid TenantId, Guid UnitId, string Period, DateTimeOffset GeneratedAtUtc);
