using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Gateway.Realtime;

public sealed class TelemetryHub : Hub
{
    public Task JoinTenant(string tenantId) => Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");

    public Task JoinUnit(string unitId) => Groups.AddToGroupAsync(Context.ConnectionId, $"unit:{unitId}");
}
