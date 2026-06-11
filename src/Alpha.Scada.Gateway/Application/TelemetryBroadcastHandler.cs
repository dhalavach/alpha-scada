using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.Telemetry.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Application;

public sealed class TelemetryBroadcastHandler(IHubContext<TelemetryHub> hub)
{
    public Task Handle(TelemetryBatchStored message, CancellationToken cancellationToken) =>
        hub.Clients.Group(TelemetryHub.TenantGroup(message.TenantId))
            .SendAsync(
                "telemetryUpdated",
                new TelemetryUpdatedPayload(
                    message.TenantId,
                    message.UnitId,
                    message.StoredAtUtc,
                    message.Samples
                        .Select(sample => new TelemetrySamplePayload(
                            sample.TagId,
                            sample.TagKey,
                            sample.Value,
                            sample.Quality,
                            sample.SourceTimestampUtc))
                        .ToArray()),
                cancellationToken);
}
