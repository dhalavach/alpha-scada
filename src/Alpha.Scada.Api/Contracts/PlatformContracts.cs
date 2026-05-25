namespace Alpha.Scada.Api.Contracts;

public sealed record TenantDto(Guid Id, string Key, string Name, string Region);

public sealed record SiteDto(Guid Id, Guid TenantId, string Key, string Name, string Region, string Status);

public sealed record UnitDto(
    Guid Id,
    Guid TenantId,
    Guid SiteId,
    string Key,
    string Name,
    string Model,
    string Status,
    DateTimeOffset? LastSeenUtc);

public sealed record TagDto(
    Guid Id,
    Guid TenantId,
    Guid UnitId,
    string Key,
    string Name,
    string Subsystem,
    string EngineeringUnit,
    double? AlarmLow,
    double? AlarmHigh);

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

public sealed record TelemetryHistoryPointDto(
    DateTimeOffset TimestampUtc,
    double Value,
    string Quality);

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

public sealed record MonthlyReportDto(
    Guid Id,
    Guid TenantId,
    Guid UnitId,
    string Period,
    double ElectricalKwh,
    double ThermalKwh,
    double RuntimeHours,
    double AvailabilityPercent,
    double EstimatedWoodChipsKg,
    double EstimatedBiocharM3,
    int AlarmCount,
    DateTimeOffset GeneratedAtUtc);

public sealed record LoginRequest(string Email, string Password);

public sealed record ReportRunRequest(Guid UnitId, string? Period);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    UserDto User);

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    string Email,
    string DisplayName,
    string Role);

public sealed record EdgeTelemetryEnvelope(
    string SchemaVersion,
    string UnitKey,
    DateTimeOffset TimestampUtc,
    IReadOnlyCollection<EdgeTelemetrySample> Samples);

public sealed record EdgeTelemetrySample(
    string TagKey,
    double Value,
    string Quality,
    DateTimeOffset SourceTimestampUtc);

public sealed record EdgeStatusEnvelope(
    string SchemaVersion,
    string UnitKey,
    string Status,
    DateTimeOffset TimestampUtc);
