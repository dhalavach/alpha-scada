using System.Text.Json;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using Wolverine;

namespace Alpha.Scada.Telemetry;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.PublishDomainEvent<TelemetryBatchStored>(Topics.TelemetryStoredEvent);
        options.ListenToNatsSubject(Topics.TelemetryWildcard)
            .UseJetStream(Topics.EdgeStream, "telemetry-edge-json")
            .DefaultIncomingMessage<TelemetryEnvelopeV1>();

        options.OnException<JsonException>().MoveToErrorQueue();
        options.OnException<InvalidTelemetryEnvelopeException>().MoveToErrorQueue();
    }
}
