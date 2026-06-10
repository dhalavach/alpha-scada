using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Alarm.Application;

public sealed class UnitStatusAlarmHandler(AlarmService service)
{
    public async Task Handle(UnitStatusChanged message, CancellationToken cancellationToken)
    {
        var route = new UnitRouteKeys(
            message.TenantId,
            message.UnitId,
            message.TenantKey,
            message.SiteKey,
            message.UnitKey);

        if (string.Equals(message.Status, "offline", StringComparison.OrdinalIgnoreCase))
        {
            await service.RaiseCommunicationLostAsync(new UnitDto(
                message.UnitId,
                message.TenantId,
                message.SiteId,
                message.UnitKey,
                message.UnitName,
                string.Empty,
                message.Status,
                message.LastSeenUtc), route, cancellationToken);
        }
        else if (string.Equals(message.Status, "online", StringComparison.OrdinalIgnoreCase))
        {
            await service.ClearCommunicationLostAsync(message.UnitId, route, cancellationToken);
        }
    }
}
