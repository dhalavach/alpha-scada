using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine;

namespace Alpha.Scada.Alarm;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.PublishOutboxRelayedDomainEvent<AlarmRaised>(Topics.AlarmRaisedEvent);
        options.PublishOutboxRelayedDomainEvent<AlarmCleared>(Topics.AlarmClearedEvent);
        options.PublishOutboxRelayedDomainEvent<AlarmAcknowledged>(Topics.AlarmAcknowledgedEvent);
        options.ListenForDomainEvent(Topics.StatusChangedEvent, "alarm-status");
        options.ListenForDomainEvent(Topics.TelemetryStoredEvent, "alarm-telemetry-stored");
    }
}
