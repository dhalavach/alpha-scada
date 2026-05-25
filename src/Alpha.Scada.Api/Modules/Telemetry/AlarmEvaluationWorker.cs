using Alpha.Scada.Api.Data;
using Alpha.Scada.Api.Modules.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Alpha.Scada.Api.Modules.Telemetry;

public sealed class AlarmEvaluationWorker(
    PlatformRepository repository,
    IHubContext<TelemetryHub> hub,
    ILogger<AlarmEvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await repository.EvaluateAlarmsAsync(stoppingToken);
                await hub.Clients.All.SendAsync("alarmsChanged", cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Alarm evaluation failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
