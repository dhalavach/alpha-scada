namespace Alpha.Scada.Telemetry.Application.Messaging;

public sealed class TelemetryResolutionException(string keyKind, string key) :
    Exception($"Telemetry {keyKind} '{key}' is not allow-listed.")
{
    public string KeyKind { get; } = keyKind;

    public string Key { get; } = key;
}
