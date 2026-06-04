namespace Alpha.Scada.Contracts;

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

public sealed record ResolveTagsRequest(Guid TenantId, Guid UnitId, IReadOnlyCollection<string> TagKeys);
