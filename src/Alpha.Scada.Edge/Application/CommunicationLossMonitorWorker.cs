using System.Net.Http.Json;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Edge.Application;

public sealed class CommunicationLossMonitorWorker(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<CommunicationLossMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("CommunicationLoss:Enabled", true))
        {
            logger.LogInformation("Communication-loss monitor is disabled.");
            return;
        }

        var intervalSeconds = configuration.GetValue("CommunicationLoss:IntervalSeconds", 30);
        var staleMinutes = configuration.GetValue("CommunicationLoss:StaleMinutes", 2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var asset = httpClientFactory.CreateClient("asset");
                var staleUnits = await asset.PostAsJsonAsync($"/internal/v1/units/offline-stale?minutes={staleMinutes}", new { }, stoppingToken);
                staleUnits.EnsureSuccessStatusCode();

                var units = await staleUnits.Content.ReadFromJsonAsync<IReadOnlyCollection<UnitDto>>(stoppingToken) ?? [];
                foreach (var unit in units)
                {
                    await httpClientFactory.CreateClient("alarm")
                        .PostAsJsonAsync("/internal/v1/alarms/communication-lost", unit, stoppingToken);

                    var notification = new RealtimeNotificationRequest(unit.TenantId, unit.Id);
                    var gateway = httpClientFactory.CreateClient("gateway");
                    var serviceToken = configuration["ServiceAuth:Token"];
                    await gateway.PostRealtimeAsync("/internal/v1/realtime/unit-status-changed", notification, serviceToken, stoppingToken);
                    await gateway.PostRealtimeAsync("/internal/v1/realtime/alarms-changed", notification, serviceToken, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Communication-loss monitor failed. Retrying.");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
