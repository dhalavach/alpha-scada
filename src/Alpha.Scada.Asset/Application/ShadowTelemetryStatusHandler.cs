using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Telemetry.Contracts;

namespace Alpha.Scada.Asset.Application;

public sealed class ShadowTelemetryStatusHandler(AssetRepository repository)
{
    public Task Handle(TelemetryBatchStored message, CancellationToken cancellationToken) =>
        repository.MarkShadowSeenAsync(message, cancellationToken);
}
