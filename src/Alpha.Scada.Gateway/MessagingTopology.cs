using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine;

namespace Alpha.Scada.Gateway;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.PublishReportRequest<ReportRequested>();
        options.ListenForReportCompleted("gateway-report-completed");
        options.ListenForEphemeralDomainEvent(Topics.AlarmRaisedEvent);
        options.ListenForEphemeralDomainEvent(Topics.AlarmClearedEvent);
        options.ListenForEphemeralDomainEvent(Topics.AlarmAcknowledgedEvent);
        options.ListenForEphemeralDomainEvent(Topics.StatusChangedEvent);
        options.ListenForEphemeralDomainEvent(Topics.TelemetryStoredEvent);
    }
}
