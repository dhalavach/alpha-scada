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
                message.TenantId,
                message.UnitId
            }, cancellationToken);
}
