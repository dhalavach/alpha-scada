namespace Alpha.Scada.Telemetry.Application;

public static class TelemetryParallelPump
{
    public static Task RunSafelyAsync<T>(
        IAsyncEnumerable<T> source,
        int maxDegreeOfParallelism,
        Func<T, CancellationToken, ValueTask> process,
        Action<Exception> onError,
        CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(
            source,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                try
                {
                    await process(item, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
            });
}
