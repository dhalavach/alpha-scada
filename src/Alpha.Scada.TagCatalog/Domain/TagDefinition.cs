namespace Alpha.Scada.TagCatalog.Domain;

public sealed record TagDefinition(
    Guid Id,
    Guid TenantId,
    Guid UnitId,
    string Key,
    string Name,
    string Subsystem,
    string EngineeringUnit,
    double? AlarmLow,
    double? AlarmHigh);
