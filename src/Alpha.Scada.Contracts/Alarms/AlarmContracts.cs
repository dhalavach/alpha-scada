namespace Alpha.Scada.Contracts;

public sealed record AlarmDto(
    Guid Id,
    Guid TenantId,
    Guid UnitId,
    Guid? TagId,
    string Severity,
    string Message,
    string State,
    DateTimeOffset RaisedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    DateTimeOffset? ClearedAtUtc);

public sealed record AlarmEvaluationRequest(
    Guid TenantId,
    Guid UnitId,
    string UnitName,
    IReadOnlyCollection<ResolvedTelemetrySample> Samples);
