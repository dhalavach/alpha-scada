/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/UnitStatusAlarmHandler.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Alarm.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class UnitStatusAlarmHandler(AlarmService service)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Handle(UnitStatusChanged message, CancellationToken cancellationToken)
    {
// LEARN: branches only when the boolean condition is true.
        if (!string.Equals(message.Status, "offline", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await service.RaiseCommunicationLostAsync(new UnitDto(
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
    }
}
