/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Application/TelemetryStoredStatusHandler.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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
