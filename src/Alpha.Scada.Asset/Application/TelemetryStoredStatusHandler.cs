/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Application/TelemetryStoredStatusHandler.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Infrastructure;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Asset.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryStoredStatusHandler(AssetService service)
{
// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<UnitStatusChanged?> Handle(TelemetryBatchStored message, CancellationToken cancellationToken) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        service.SetUnitOnlineAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.UnitId,
// LEARN: creates a new object or record instance.
            new UnitStatusRoute(message.TenantKey, message.SiteKey, message.UnitKey),
// LEARN: executes one C# statement; semicolons terminate most statements.
            cancellationToken);
}
