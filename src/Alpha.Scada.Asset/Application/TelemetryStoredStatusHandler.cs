using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Asset.Infrastructure;

namespace Alpha.Scada.Asset.Application;

public sealed class TelemetryStoredStatusHandler(AssetService service)
{
    public Task<UnitStatusChanged?> Handle(TelemetryBatchStored message, CancellationToken cancellationToken) =>
        service.SetUnitOnlineAsync(
            message.UnitId,
            new UnitStatusRoute(message.TenantKey, message.SiteKey, message.UnitKey),
            cancellationToken);
}
