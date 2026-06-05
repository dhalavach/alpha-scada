/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/Topics.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class Topics
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string Telemetry(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.telemetry";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string Status(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.status";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string AlarmRaised(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.alarm.raised";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string AlarmCleared(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.alarm.cleared";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string AlarmAcknowledged(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.alarm.acknowledged";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string TelemetryStored(string tenant, string site, string unit) =>
        $"alpha.{tenant}.{site}.{unit}.telemetry-stored";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string EdgeMqttTelemetry(string tenant, string site, string unit) =>
        $"alpha/{tenant}/{site}/{unit}/telemetry";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
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

    public const string EdgeStream = "ALPHA_EDGE";
    public const string DomainStream = "ALPHA_DOMAIN";
    public const string JobsStream = "ALPHA_JOBS";

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string Dlq(string service, string originalSubject) =>
        $"alpha._dlq.{service}.{originalSubject}";
}
