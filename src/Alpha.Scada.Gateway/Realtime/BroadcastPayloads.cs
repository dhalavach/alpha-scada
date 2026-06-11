namespace Alpha.Scada.Gateway.Realtime;

public sealed record TelemetryUpdatedPayload(
    Guid TenantId,
    Guid UnitId,
    DateTimeOffset StoredAtUtc,
    IReadOnlyList<TelemetrySamplePayload> Samples);

public sealed record TelemetrySamplePayload(
    Guid TagId,
    string TagKey,
    double Value,
    string Quality,
    DateTimeOffset TimestampUtc);

public sealed record UnitStatusChangedPayload(
    Guid TenantId,
    Guid UnitId,
    string Status,
    DateTimeOffset? LastSeenUtc);

public sealed record AlarmsChangedPayload(Guid TenantId, Guid UnitId);

public sealed record ReportCompletedPayload(
    Guid RequestId,
    Guid ReportId,
    Guid TenantId,
    Guid UnitId,
    string Period,
    DateTimeOffset CompletedAtUtc);
