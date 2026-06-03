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
        options.ListenForDomainEvent(Topics.ReportCompleted, "gateway-report-completed");
        options.ListenForDomainEvent(Topics.AlarmRaisedEvent, "gateway-alarm-raised");
        options.ListenForDomainEvent(Topics.AlarmClearedEvent, "gateway-alarm-cleared");
        options.ListenForDomainEvent(Topics.AlarmAcknowledgedEvent, "gateway-alarm-acknowledged");
        options.ListenForDomainEvent(Topics.StatusChangedEvent, "gateway-status");
        options.ListenForDomainEvent(Topics.TelemetryStoredEvent, "gateway-telemetry-stored");
    }
}
