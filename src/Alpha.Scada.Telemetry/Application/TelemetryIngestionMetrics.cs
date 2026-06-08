using System.Diagnostics;
using System.Globalization;
using System.Text;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Telemetry.Application;

public enum TelemetryIngestionOutcome
{
    Success,
    Dropped,
    DeadLetter,
    Retry,
    TerminalError,
    EscapedError
}

public sealed class TelemetryIngestionMetrics : IAlphaMetricsProvider
{
    private static readonly double[] DurationBucketsSeconds = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];
    private readonly long[] durationBuckets = new long[DurationBucketsSeconds.Length];
    private long inFlight;
    private long success;
    private long dropped;
    private long deadLetter;
    private long retry;
    private long terminalError;
    private long escapedError;
    private long durationCount;
    private long durationTicksTotal;

    public TelemetryIngestionMeasurement Begin()
    {
        Interlocked.Increment(ref inFlight);
        return new TelemetryIngestionMeasurement(this, Stopwatch.GetTimestamp());
    }

    public void AppendMetrics(StringBuilder metrics, string serviceName)
    {
        var serviceLabel = EscapeLabel(serviceName);
        metrics.AppendLine("# HELP alpha_scada_telemetry_ingestion_in_flight Telemetry messages currently being processed");
        metrics.AppendLine("# TYPE alpha_scada_telemetry_ingestion_in_flight gauge");
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_telemetry_ingestion_in_flight{{service=\"{serviceLabel}\"}} {Interlocked.Read(ref inFlight)}");
        metrics.AppendLine("# HELP alpha_scada_telemetry_ingestion_messages_total Telemetry ingestion messages by terminal outcome");
        metrics.AppendLine("# TYPE alpha_scada_telemetry_ingestion_messages_total counter");
        AppendOutcome(metrics, serviceLabel, "success", ref success);
        AppendOutcome(metrics, serviceLabel, "dropped", ref dropped);
        AppendOutcome(metrics, serviceLabel, "dead_letter", ref deadLetter);
        AppendOutcome(metrics, serviceLabel, "retry", ref retry);
        AppendOutcome(metrics, serviceLabel, "terminal_error", ref terminalError);
        AppendOutcome(metrics, serviceLabel, "escaped_error", ref escapedError);
        metrics.AppendLine("# HELP alpha_scada_telemetry_ingestion_processing_seconds Telemetry ingestion processing duration");
        metrics.AppendLine("# TYPE alpha_scada_telemetry_ingestion_processing_seconds histogram");

        long cumulative = 0;
        for (var i = 0; i < DurationBucketsSeconds.Length; i++)
        {
            cumulative += Interlocked.Read(ref durationBuckets[i]);
            metrics.AppendLine(
                CultureInfo.InvariantCulture,
                $"alpha_scada_telemetry_ingestion_processing_seconds_bucket{{service=\"{serviceLabel}\",le=\"{DurationBucketsSeconds[i]}\"}} {cumulative}");
        }

        var count = Interlocked.Read(ref durationCount);
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_telemetry_ingestion_processing_seconds_bucket{{service=\"{serviceLabel}\",le=\"+Inf\"}} {count}");
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_telemetry_ingestion_processing_seconds_count{{service=\"{serviceLabel}\"}} {count}");
        metrics.AppendLine(
            CultureInfo.InvariantCulture,
            $"alpha_scada_telemetry_ingestion_processing_seconds_sum{{service=\"{serviceLabel}\"}} {Interlocked.Read(ref durationTicksTotal) / (double)Stopwatch.Frequency}");
    }

    private void Complete(long startedTimestamp, TelemetryIngestionOutcome outcome)
    {
        Interlocked.Decrement(ref inFlight);
        IncrementOutcome(outcome);

        var elapsedTicks = Stopwatch.GetTimestamp() - startedTimestamp;
        Interlocked.Increment(ref durationCount);
        Interlocked.Add(ref durationTicksTotal, elapsedTicks);
        var elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
        for (var i = 0; i < DurationBucketsSeconds.Length; i++)
        {
            if (elapsedSeconds <= DurationBucketsSeconds[i])
            {
                Interlocked.Increment(ref durationBuckets[i]);
                return;
            }
        }
    }

    private void IncrementOutcome(TelemetryIngestionOutcome outcome)
    {
        switch (outcome)
        {
            case TelemetryIngestionOutcome.Success:
                Interlocked.Increment(ref success);
                break;
            case TelemetryIngestionOutcome.Dropped:
                Interlocked.Increment(ref dropped);
                break;
            case TelemetryIngestionOutcome.DeadLetter:
                Interlocked.Increment(ref deadLetter);
                break;
            case TelemetryIngestionOutcome.Retry:
                Interlocked.Increment(ref retry);
                break;
            case TelemetryIngestionOutcome.TerminalError:
                Interlocked.Increment(ref terminalError);
                break;
            case TelemetryIngestionOutcome.EscapedError:
                Interlocked.Increment(ref escapedError);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported telemetry ingestion outcome.");
        }
    }

    private static void AppendOutcome(StringBuilder metrics, string serviceLabel, string outcome, ref long value) =>
        metrics.AppendLine(
            CultureInfo.InvariantCulture,
            $"alpha_scada_telemetry_ingestion_messages_total{{service=\"{serviceLabel}\",outcome=\"{outcome}\"}} {Interlocked.Read(ref value)}");

    private static string EscapeLabel(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    public readonly struct TelemetryIngestionMeasurement(TelemetryIngestionMetrics owner, long startedTimestamp)
    {
        public void Complete(TelemetryIngestionOutcome outcome) =>
            owner.Complete(startedTimestamp, outcome);
    }
}
