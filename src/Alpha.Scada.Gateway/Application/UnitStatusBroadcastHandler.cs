using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Gateway.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Application;

public sealed class UnitStatusBroadcastHandler(IHubContext<TelemetryHub> hub)
{
    public Task Handle(UnitStatusChanged message, CancellationToken cancellationToken) =>
        hub.Clients.Group(TelemetryHub.TenantGroup(message.TenantId))
            .SendAsync(
                "unitStatusChanged",
                new UnitStatusChangedPayload(
                    message.TenantId,
                    message.UnitId,
                    message.Status,
                    message.LastSeenUtc),
                cancellationToken);
}
