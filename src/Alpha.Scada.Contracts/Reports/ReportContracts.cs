/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Contracts/Reports/ReportContracts.cs
- Module role: Alpha.Scada.Contracts is the shared DTO contract package. These types describe REST payloads and edge wire formats that cross service or process boundaries.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Contracts;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record MonthlyReportDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Period,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double ElectricalKwh,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double ThermalKwh,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double RuntimeHours,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double AvailabilityPercent,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double EstimatedWoodChipsKg,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double EstimatedBiocharM3,
// LEARN: continues an argument/object/collection initializer onto the next line.
    int AlarmCount,
// LEARN: executes one C# statement; semicolons terminate most statements.
    DateTimeOffset GeneratedAtUtc);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ReportRunRequest(Guid UnitId, string? Period);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ReportAggregateDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
    double ElectricalKwh,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double ThermalKwh,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double RuntimeHours,
// LEARN: executes one C# statement; semicolons terminate most statements.
    double EstimatedWoodChipsKg);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ReportMetricBindingDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
    string MetricKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string AggregationType,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double Scale,
// LEARN: executes one C# statement; semicolons terminate most statements.
    double? Threshold);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ReportProfileDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double AvailabilityNoAlarmsPercent,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double AvailabilityWithAlarmsPercent,
// LEARN: continues an argument/object/collection initializer onto the next line.
    double BiocharYieldM3PerKg,
// LEARN: executes one C# statement; semicolons terminate most statements.
    IReadOnlyCollection<ReportMetricBindingDto> MetricBindings);

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record ReportAggregateRequest(
// LEARN: continues an argument/object/collection initializer onto the next line.
    string Period,
// LEARN: executes one C# statement; semicolons terminate most statements.
    IReadOnlyCollection<ReportMetricBindingDto> MetricBindings);

// LEARN: declares a static helper class whose members are called on the type itself.
public static class ReportMetricKeys
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string ElectricalKwh = "electrical_kwh";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string ThermalKwh = "thermal_kwh";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string RuntimeHours = "runtime_hours";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string WoodChipsKg = "wood_chips_kg";
}
