namespace Alpha.Scada.Telemetry.Application.Messaging;

public interface ITelemetryAdapter
{
    bool CanHandle(TelemetrySource source);

    CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source);
}

public sealed record TelemetrySource(string Subject, IReadOnlyDictionary<string, string?> Headers);

public sealed class TelemetryAdapterResolver(IEnumerable<ITelemetryAdapter> adapters)
{
    private readonly IReadOnlyCollection<ITelemetryAdapter> adapters = adapters.ToArray();

    public CanonicalTelemetry Normalize(ReadOnlyMemory<byte> payload, TelemetrySource source)
    {
        var adapter = adapters.FirstOrDefault(candidate => candidate.CanHandle(source))
            ?? throw new InvalidTelemetryEnvelopeException($"No telemetry adapter can handle subject '{source.Subject}'.");

        return adapter.Normalize(payload, source);
    }
}
