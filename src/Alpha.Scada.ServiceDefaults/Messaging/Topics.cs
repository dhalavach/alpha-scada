namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class Topics
{
    public static string Telemetry(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.telemetry";

    public static string Status(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.status";

    public static string AlarmRaised(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.alarm.raised";

    public static string AlarmCleared(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.alarm.cleared";

    public static string AlarmAcknowledged(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.alarm.acknowledged";

    public static string TelemetryStored(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.telemetry-stored";

    public static string EdgeMqttTelemetry(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/telemetry";

    public static string EdgeMqttStatus(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/status";

    public const string TelemetryWildcard = "alpha.*.*.*.telemetry";
    public const string StatusWildcard = "alpha.*.*.*.status";
    public const string AlarmWildcard = "alpha.*.*.*.alarm.*";
    public const string TelemetryStoredWildcard = "alpha.*.*.*.telemetry-stored";
    public const string SparkplugWildcard = "spBv1.0.>";
    public const string TelemetryStoredEvent = "alpha.telemetry.stored";
    public const string StatusChangedEvent = "alpha.status.changed";
    public const string AlarmRaisedEvent = "alpha.alarm.raised";
    public const string AlarmClearedEvent = "alpha.alarm.cleared";
    public const string AlarmAcknowledgedEvent = "alpha.alarm.acknowledged";
    public const string ReportRequested = "alpha.report.requested";
    public const string ReportCompleted = "alpha.report.completed";
    public const string DlqWildcard = "alpha_dlq.>";

    public const string EdgeStream = "ALPHA_EDGE";
    public const string DomainStream = "ALPHA_DOMAIN";
    public const string JobsStream = "ALPHA_JOBS";
    public const string DlqStream = "ALPHA_DLQ";

    public static string Dlq(string service, string originalSubject) =>
        $"alpha_dlq.{service}.{originalSubject}";
}
