namespace Alpha.Scada.Contracts;

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

public sealed record TagValueDto(
    Guid TagId,
    Guid TenantId,
    Guid UnitId,
    double Value,
    string Quality,
    DateTimeOffset TimestampUtc);

public sealed record TelemetryHistoryPointDto(DateTimeOffset TimestampUtc, double Value, string Quality);

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

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserDto User);

public sealed record UserDto(Guid Id, Guid TenantId, string Email, string DisplayName, string Role);

public sealed record CurrentUserDto(Guid UserId, Guid TenantId, string Email, string DisplayName, string Role);

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

public sealed record EdgeStatusEnvelope(string SchemaVersion, string UnitKey, string Status, DateTimeOffset TimestampUtc);

public sealed record ResolvedUnitDto(Guid TenantId, Guid SiteId, Guid UnitId, string UnitName, string UnitStatus);

public sealed record UnitRouteDto(Guid TenantId, Guid SiteId, Guid UnitId, string SiteKey, string UnitKey, string UnitName);

public sealed record ResolveTagsRequest(Guid TenantId, Guid UnitId, IReadOnlyCollection<string> TagKeys);

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

public sealed record AlarmEvaluationRequest(
    Guid TenantId,
    Guid UnitId,
    string UnitName,
    IReadOnlyCollection<ResolvedTelemetrySample> Samples);

public sealed record ReportAggregateDto(
    double ElectricalKwh,
    double ThermalKwh,
    double RuntimeHours,
    double EstimatedWoodChipsKg);

public sealed record ReportMetricBindingDto(
    string MetricKey,
    Guid TagId,
    string AggregationType,
    double Scale,
    double? Threshold);

public sealed record ReportProfileDto(
    Guid TenantId,
    Guid UnitId,
    double AvailabilityNoAlarmsPercent,
    double AvailabilityWithAlarmsPercent,
    double BiocharYieldM3PerKg,
    IReadOnlyCollection<ReportMetricBindingDto> MetricBindings);

public sealed record ReportAggregateRequest(
    string Period,
    IReadOnlyCollection<ReportMetricBindingDto> MetricBindings);

public static class ReportMetricKeys
{
    public const string ElectricalKwh = "electrical_kwh";
    public const string ThermalKwh = "thermal_kwh";
    public const string RuntimeHours = "runtime_hours";
    public const string WoodChipsKg = "wood_chips_kg";
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";
    public const string SupportEngineer = "SupportEngineer";
}

public static class RoleRules
{
    public static bool CanAcknowledge(string role) => role is Roles.Admin or Roles.Operator or Roles.SupportEngineer;

    public static bool CanManageConfiguration(string role) => role is Roles.Admin or Roles.SupportEngineer;

    public static bool IsSupport(string role) => role == Roles.SupportEngineer;
}
