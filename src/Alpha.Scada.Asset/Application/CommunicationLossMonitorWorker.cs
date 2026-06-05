/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Application/CommunicationLossMonitorWorker.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is a hosted background process. ASP.NET Core starts it with the service and stops it with the host cancellation token.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: BackgroundService is the .NET hosted-worker base class; ExecuteAsync runs until the host cancellation token is signaled. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Asset.Application;

public sealed class CommunicationLossMonitorWorker(
    IConfiguration configuration,
    AssetService service,
    Wolverine.IMessageBus bus,
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
                    foreach (var offlineUnit in offlineUnits)
                    {
                        await bus.PublishAsync(offlineUnit);
                    }

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
