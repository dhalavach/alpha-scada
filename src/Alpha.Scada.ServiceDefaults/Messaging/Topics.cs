namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class Topics
{
    public static string Telemetry(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/telemetry";

    public static string Status(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/status";

    public static string AlarmRaised(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/alarm/raised";

    public static string AlarmCleared(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/alarm/cleared";

    public static string AlarmAcknowledged(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/alarm/acknowledged";

    public static string TelemetryStored(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/telemetry-stored";

    public const string TelemetryWildcard = "alpha/+/+/+/telemetry";
    public const string StatusWildcard = "alpha/+/+/+/status";
    public const string AlarmWildcard = "alpha/+/+/+/alarm/+";
    public const string TelemetryStoredWildcard = "alpha/+/+/+/telemetry-stored";

    public static string Dlq(string service, string originalTopic) =>
        $"alpha/_dlq/{service}/{originalTopic}";
}
