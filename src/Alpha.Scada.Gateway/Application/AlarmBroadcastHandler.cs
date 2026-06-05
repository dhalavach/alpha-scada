/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Gateway/Application/AlarmBroadcastHandler.cs
- Module role: Alpha.Scada.Gateway is the public boundary/BFF. It keeps the React UI talking to one API surface, owns SignalR realtime fan-out, and translates browser-facing requests into calls or messages for backend services.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: SignalR provides server-to-browser realtime delivery; Gateway owns that public realtime boundary.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Gateway.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmBroadcastHandler(IHubContext<TelemetryHub> hub)
{
// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task Handle(AlarmRaised message, CancellationToken cancellationToken) =>
        BroadcastAsync(message.TenantId, message.UnitId, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task Handle(AlarmCleared message, CancellationToken cancellationToken) =>
        BroadcastAsync(message.TenantId, message.UnitId, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task Handle(AlarmAcknowledged message, CancellationToken cancellationToken) =>
        BroadcastAsync(message.TenantId, message.UnitId, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    private Task BroadcastAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken) =>
        hub.Clients.Group(TelemetryHub.TenantGroup(tenantId))
            .SendAsync("alarmsChanged", new { TenantId = tenantId, UnitId = unitId }, cancellationToken);
}
