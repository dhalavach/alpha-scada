/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Gateway/Application/ReportCompletedHandler.cs
- Module role: Alpha.Scada.Gateway is the public boundary/BFF. It keeps the React UI talking to one API surface, owns SignalR realtime fan-out, and translates browser-facing requests into calls or messages for backend services.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: SignalR provides server-to-browser realtime delivery; Gateway owns that public realtime boundary.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.Reporting.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Application;

public sealed class ReportCompletedHandler(IHubContext<TelemetryHub> hub)
{
    public Task Handle(ReportCompleted message, CancellationToken cancellationToken) =>
        hub.Clients.Group(TelemetryHub.TenantGroup(message.TenantId))
            .SendAsync("reportCompleted", new
            {
                message.RequestId,
                message.ReportId,
                message.TenantId,
                message.UnitId,
                message.Period,
                message.CompletedAtUtc
            }, cancellationToken);
}
