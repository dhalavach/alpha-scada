using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Gateway.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Application;

public sealed class AlarmBroadcastHandler(IHubContext<TelemetryHub> hub)
{
    public Task Handle(AlarmRaised message, CancellationToken cancellationToken) =>
        BroadcastAsync(message.TenantId, message.UnitId, cancellationToken);

    public Task Handle(AlarmCleared message, CancellationToken cancellationToken) =>
        BroadcastAsync(message.TenantId, message.UnitId, cancellationToken);

    public Task Handle(AlarmAcknowledged message, CancellationToken cancellationToken) =>
        BroadcastAsync(message.TenantId, message.UnitId, cancellationToken);

    private Task BroadcastAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken) =>
        hub.Clients.Group(TelemetryHub.TenantGroup(tenantId))
            .SendAsync("alarmsChanged", new AlarmsChangedPayload(tenantId, unitId), cancellationToken);
}
