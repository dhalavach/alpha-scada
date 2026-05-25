using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Api.Modules.Realtime;

public sealed class TelemetryHub : Hub
{
    public async Task JoinTenant(string tenantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
    }

    public async Task JoinUnit(string unitId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"unit:{unitId}");
    }
}
