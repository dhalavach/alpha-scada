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

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class CommunicationLossMonitorWorker(
    IConfiguration configuration,
    AssetService service,
    Wolverine.IMessageBus bus,
// LEARN: inherits from BackgroundService, the ASP.NET Core base type for long-running hosted workers.
    ILogger<CommunicationLossMonitorWorker> logger) : BackgroundService
{
// LEARN: overrides the hosted-worker entry point; the host calls this when the service starts.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
// LEARN: branches only when the boolean condition is true.
        if (!configuration.GetValue("CommunicationLoss:Enabled", true))
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogInformation("Communication-loss monitor is disabled.");
            return;
        }

        var intervalSeconds = configuration.GetValue("CommunicationLoss:IntervalSeconds", 30);
        var staleMinutes = configuration.GetValue("CommunicationLoss:StaleMinutes", 2);

// LEARN: starts a loop that continues while its condition remains true.
        while (!stoppingToken.IsCancellationRequested)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
                var offlineUnits = await service.MarkStaleUnitsOfflineAsync(staleMinutes, stoppingToken);
// LEARN: branches only when the boolean condition is true.
                if (offlineUnits.Count > 0)
                {
// LEARN: loops over each item in a collection.
                    foreach (var offlineUnit in offlineUnits)
                    {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                        await bus.PublishAsync(offlineUnit);
                    }

// LEARN: writes structured log output; placeholders become searchable log fields.
                    logger.LogWarning("Marked {Count} stale units offline after {Minutes} minutes without telemetry.", offlineUnits.Count, staleMinutes);
                }
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (Exception ex)
            {
// LEARN: writes structured log output; placeholders become searchable log fields.
                logger.LogWarning(ex, "Communication-loss monitor failed. Retrying.");
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
