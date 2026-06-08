namespace Alpha.Scada.Telemetry.Application;

public sealed class TelemetryIngestionOptions
{
    public const string SectionName = "Telemetry:Ingestion";
    public const int DefaultMaxDegreeOfParallelism = 8;
    public const int MinMaxDegreeOfParallelism = 1;
    public const int MaxMaxDegreeOfParallelism = 64;

    public int MaxDegreeOfParallelism { get; init; } = DefaultMaxDegreeOfParallelism;

    public int EffectiveMaxDegreeOfParallelism =>
        Math.Clamp(MaxDegreeOfParallelism, MinMaxDegreeOfParallelism, MaxMaxDegreeOfParallelism);

    public static TelemetryIngestionOptions FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<TelemetryIngestionOptions>() ?? new TelemetryIngestionOptions();
}
