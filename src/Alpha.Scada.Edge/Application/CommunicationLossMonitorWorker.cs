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

                _ = await staleUnits.Content.ReadFromJsonAsync<IReadOnlyCollection<UnitDto>>(stoppingToken) ?? [];
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
