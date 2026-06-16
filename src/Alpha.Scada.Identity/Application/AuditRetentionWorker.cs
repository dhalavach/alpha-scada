using Alpha.Scada.Identity.Infrastructure;

namespace Alpha.Scada.Identity.Application;

public sealed record AuditRetentionOptions(int RetentionDays)
{
    public const int DefaultRetentionDays = 180;

    public static AuditRetentionOptions FromConfiguration(IConfiguration configuration)
    {
        var retentionDays = configuration.GetValue("Audit:RetentionDays", DefaultRetentionDays);
        if (retentionDays <= 0)
        {
            throw new InvalidOperationException("Audit:RetentionDays must be greater than zero.");
        }

        return new AuditRetentionOptions(retentionDays);
    }
}

public sealed class AuditRetentionWorker(
    IdentityRepository repository,
    AuditRetentionOptions options,
    ILogger<AuditRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PruneSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PruneSafelyAsync(stoppingToken);
        }
    }

    private async Task PruneSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await repository.DeleteAuditEventsOlderThanAsync(
                options.RetentionDays,
                cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation(
                    "Deleted {DeletedCount} audit events older than {RetentionDays} days.",
                    deleted,
                    options.RetentionDays);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Audit retention sweep failed.");
        }
    }
}
