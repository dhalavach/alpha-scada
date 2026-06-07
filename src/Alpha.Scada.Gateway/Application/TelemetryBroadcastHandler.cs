using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.Telemetry.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Application;

public sealed class TelemetryBroadcastHandler(IHubContext<TelemetryHub> hub)
{
    public Task Handle(TelemetryBatchStored message, CancellationToken cancellationToken) =>
        hub.Clients.Group(TelemetryHub.TenantGroup(message.TenantId))
            .SendAsync("telemetryUpdated", new
            {
                tenantId = message.TenantId,
                unitId = message.UnitId,
                storedAtUtc = message.StoredAtUtc,
                samples = message.Samples.Select(sample => new
                {
                    tagId = sample.TagId,
                    tagKey = sample.TagKey,
                    value = sample.Value,
                    quality = sample.Quality,
                    timestampUtc = sample.SourceTimestampUtc
                }).ToArray()
            }, cancellationToken);
}
