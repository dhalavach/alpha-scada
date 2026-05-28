using System.Text.Json.Serialization;

namespace Alpha.Scada.Contracts.Messaging;

public sealed record TelemetryEnvelopeV1(
    [property: JsonPropertyName("schemaVersion")] string PayloadSchemaVersion,
    string UnitKey,
    DateTimeOffset TimestampUtc,
    IReadOnlyCollection<TelemetrySampleV1> Samples)
{
    public const string SchemaVersion = "1.0";
}

public sealed record TelemetrySampleV1(
    string TagKey,
    double Value,
    string Quality,
    DateTimeOffset SourceTimestampUtc);
