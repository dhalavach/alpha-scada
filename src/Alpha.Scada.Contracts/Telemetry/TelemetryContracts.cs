namespace Alpha.Scada.Contracts;

public sealed record TagCurrentDto(
    Guid TagId,
    Guid TenantId,
    Guid UnitId,
    string TagKey,
    string Name,
    string Subsystem,
    double Value,
    string EngineeringUnit,
    string Quality,
    DateTimeOffset TimestampUtc);

public sealed record TagValueDto(
    Guid TagId,
    Guid TenantId,
    Guid UnitId,
    double Value,
    string Quality,
    DateTimeOffset TimestampUtc);

public sealed record TelemetryHistoryPointDto(DateTimeOffset TimestampUtc, double Value, string Quality);

public sealed record ResolvedTelemetrySample(
    Guid TagId,
    string TagKey,
    string TagName,
    string Subsystem,
    string EngineeringUnit,
    double? AlarmLow,
    double? AlarmHigh,
    double Value,
    string Quality,
    DateTimeOffset SourceTimestampUtc);

public sealed record TelemetryIngestRequest(
    Guid TenantId,
    Guid UnitId,
    IReadOnlyCollection<ResolvedTelemetrySample> Samples);
