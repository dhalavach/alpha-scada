using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;
using Wolverine;

namespace Alpha.Scada.Alarm.Application;

public sealed class UnitStatusAlarmHandler(AlarmService service, IMessageBus bus)
{
    public async Task Handle(UnitStatusChanged message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.Status, "offline", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var raised = await service.RaiseCommunicationLostAsync(new UnitDto(
            message.UnitId,
            message.TenantId,
            message.SiteId,
            message.UnitKey,
            message.UnitName,
            string.Empty,
            message.Status,
            message.LastSeenUtc), new UnitRouteKeys(
                message.TenantId,
                message.UnitId,
                message.TenantKey,
                message.SiteKey,
                message.UnitKey), cancellationToken);
        if (raised is not null)
        {
            await bus.PublishAsync(raised);
        }
    }
}
