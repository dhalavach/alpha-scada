/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Contracts/Reports/ReportContracts.cs
- Module role: Alpha.Scada.Contracts is the shared DTO contract package. These types describe REST payloads and edge wire formats that cross service or process boundaries.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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
