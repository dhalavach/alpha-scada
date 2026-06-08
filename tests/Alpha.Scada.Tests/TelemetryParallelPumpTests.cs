using System.Runtime.CompilerServices;
using Alpha.Scada.Telemetry.Application;

namespace Alpha.Scada.Tests;

public sealed class TelemetryParallelPumpTests
{
    [Fact]
    public async Task Pump_reaches_parallelism_without_exceeding_configured_limit()
    {
        const int maxDegreeOfParallelism = 3;
        var current = 0;
        var maxObserved = 0;

        await TelemetryParallelPump.RunSafelyAsync(
            Items(12),
            maxDegreeOfParallelism,
            async (_, ct) =>
            {
                var inFlight = Interlocked.Increment(ref current);
                UpdateMax(ref maxObserved, inFlight);
                await Task.Delay(75, ct);
                Interlocked.Decrement(ref current);
            },
            _ => { },
            CancellationToken.None);

        Assert.True(maxObserved > 1);
        Assert.True(maxObserved <= maxDegreeOfParallelism);
    }

    [Fact]
    public async Task Pump_logs_error_and_continues_after_non_shutdown_exception()
    {
        var processed = new List<int>();
        var errors = 0;

        await TelemetryParallelPump.RunSafelyAsync(
            Items(5),
            2,
            (item, _) =>
            {
                if (item == 2)
                {
                    throw new InvalidOperationException("boom");
                }

                lock (processed)
                {
                    processed.Add(item);
                }

                return ValueTask.CompletedTask;
            },
            _ => Interlocked.Increment(ref errors),
            CancellationToken.None);

        Assert.Equal(1, errors);
        Assert.Contains(0, processed);
        Assert.Contains(1, processed);
        Assert.Contains(3, processed);
        Assert.Contains(4, processed);
    }

    private static void UpdateMax(ref int maxObserved, int current)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref maxObserved);
            if (current <= snapshot || Interlocked.CompareExchange(ref maxObserved, current, snapshot) == snapshot)
            {
                return;
            }
        }
    }

    private static async IAsyncEnumerable<int> Items(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}
