namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class Topics
{
    public static string Telemetry(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.telemetry";

    public const string TelemetryWildcard = "alpha.*.*.*.telemetry";
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
    public const string ReportsStream = "ALPHA_REPORTS";
    public const string JobsStream = "ALPHA_JOBS";
    public const string DlqStream = "ALPHA_DLQ";

    public static string Dlq(string service, string originalSubject) =>
        $"alpha_dlq.{service}.{originalSubject}";
}
