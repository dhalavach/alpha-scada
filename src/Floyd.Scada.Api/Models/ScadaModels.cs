namespace Floyd.Scada.Api.Models;

public sealed record UnitSummary(
    string UnitId,
    string Name,
    string Model,
    string SiteName,
    string State,
    DateTimeOffset LastSeenUtc);

public sealed record TagDefinition(
    string Key,
    string Name,
    string Subsystem,
    string EngineeringUnit,
    double? AlarmLow,
    double? AlarmHigh);

public sealed record TagSample(
    string TagKey,
    string Name,
    string Subsystem,
    double Value,
    string EngineeringUnit,
    string Quality,
    DateTimeOffset TimestampUtc);

public sealed record AlarmEvent(
    string Id,
    string TagKey,
    string Name,
    string Severity,
    string Message,
    DateTimeOffset RaisedAtUtc,
    bool Active);

public sealed record MonthlyReportSummary(
    string UnitId,
    string Period,
    double ElectricalKwh,
    double ThermalKwh,
    double RuntimeHours,
    double AvailabilityPercent,
    double EstimatedWoodChipsKg,
    double EstimatedBiocharM3,
    int AlarmCount);
