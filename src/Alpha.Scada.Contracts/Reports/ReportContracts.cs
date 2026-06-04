namespace Alpha.Scada.Contracts;

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

public sealed record ReportRunRequest(Guid UnitId, string? Period);

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
