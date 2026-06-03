using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine;

namespace Alpha.Scada.Alarm;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.PublishDomainEvent<AlarmRaised>(Topics.AlarmRaisedEvent);
        options.PublishDomainEvent<AlarmCleared>(Topics.AlarmClearedEvent);
        options.PublishDomainEvent<AlarmAcknowledged>(Topics.AlarmAcknowledgedEvent);
        options.ListenForDomainEvent(Topics.StatusChangedEvent, "alarm-status");
        options.ListenForDomainEvent(Topics.TelemetryStoredEvent, "alarm-telemetry-stored");
    }
}
