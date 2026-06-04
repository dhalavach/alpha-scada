namespace Alpha.Scada.Contracts;

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
