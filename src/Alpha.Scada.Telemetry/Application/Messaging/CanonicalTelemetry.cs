namespace Alpha.Scada.Telemetry.Application.Messaging;

public sealed record CanonicalTelemetry(
    string TenantKey,
    string SiteKey,
    string UnitKey,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyCollection<CanonicalReading> Readings);

public sealed record CanonicalReading(
    string TagKey,
    double Value,
    string Quality,
    DateTimeOffset SourceTimestampUtc);
