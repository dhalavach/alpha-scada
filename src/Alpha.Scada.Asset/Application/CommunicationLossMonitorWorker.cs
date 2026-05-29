namespace Alpha.Scada.Asset.Application;

public sealed class CommunicationLossMonitorWorker(
    IConfiguration configuration,
    AssetService service,
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
                var offlineUnits = await service.MarkStaleUnitsOfflineAsync(staleMinutes, stoppingToken);
                if (offlineUnits.Count > 0)
                {
                    logger.LogWarning("Marked {Count} stale units offline after {Minutes} minutes without telemetry.", offlineUnits.Count, staleMinutes);
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
