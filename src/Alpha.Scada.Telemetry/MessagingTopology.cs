using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine;

namespace Alpha.Scada.Telemetry;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.PublishDomainEvent<TelemetryBatchStored>(Topics.TelemetryStoredEvent);
    }
}
