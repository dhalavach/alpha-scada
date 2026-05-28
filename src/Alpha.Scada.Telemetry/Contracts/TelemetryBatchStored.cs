namespace Alpha.Scada.Telemetry.Contracts;

public sealed record TelemetryBatchStored(
    Guid TenantId,
    Guid UnitId,
    string TenantKey,
    string SiteKey,
    string UnitKey,
    DateTimeOffset StoredAtUtc,
    IReadOnlyList<StoredSample> Samples)
{
    public const string SchemaVersion = "1.0";
}

public sealed record StoredSample(
    Guid TagId,
    string TagKey,
    double Value,
    string Quality,
    DateTimeOffset SourceTimestampUtc);
