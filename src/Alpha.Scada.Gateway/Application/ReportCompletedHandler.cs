using Alpha.Scada.Gateway.Realtime;
using Alpha.Scada.Reporting.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Application;

public sealed class ReportCompletedHandler(IHubContext<TelemetryHub> hub)
{
    public Task Handle(ReportCompleted message, CancellationToken cancellationToken) =>
        hub.Clients.Group(TelemetryHub.TenantGroup(message.TenantId))
            .SendAsync(
                "reportCompleted",
                new ReportCompletedPayload(
                    message.RequestId,
                    message.ReportId,
                    message.TenantId,
                    message.UnitId,
                    message.Period,
                    message.CompletedAtUtc),
                cancellationToken);
}
