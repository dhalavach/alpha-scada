using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine;

namespace Alpha.Scada.Asset;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.PublishDomainEvent<UnitStatusChanged>(Topics.StatusChangedEvent);
        options.ListenForDomainEvent(Topics.TelemetryStoredEvent, "asset-telemetry-stored");
    }
}
