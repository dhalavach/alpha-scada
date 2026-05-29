using Alpha.Scada.Telemetry.Contracts;

namespace Alpha.Scada.Asset.Application;

public sealed class TelemetryStoredStatusHandler(AssetService service)
{
    public Task Handle(TelemetryBatchStored message, CancellationToken cancellationToken) =>
        service.SetUnitOnlineAsync(message.UnitId, cancellationToken);
}
