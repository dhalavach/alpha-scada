using System.Diagnostics;
using System.Diagnostics.Metrics;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Telemetry.Application.Messaging;

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

public sealed class TelemetryIngestionMetrics
{
    private const string ServiceName = "alpha-scada-telemetry";
    private static readonly Meter Meter = new(AlphaObservability.TelemetryMeterName);
    private static readonly ActivitySource Activities = new(AlphaObservability.TelemetryMeterName);
    private static readonly Counter<long> Messages = Meter.CreateCounter<long>(
        "alpha.scada.telemetry.ingestion.messages",
        description: "Telemetry ingestion messages by terminal outcome");
    private static readonly UpDownCounter<long> InFlight = Meter.CreateUpDownCounter<long>(
        "alpha.scada.telemetry.ingestion.in_flight",
        description: "Telemetry messages currently being processed");
    private static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "alpha.scada.telemetry.ingestion.processing",
        unit: "s",
        description: "Telemetry ingestion processing duration");
    private static readonly Counter<long> MaxDeliveriesExhausted = Meter.CreateCounter<long>(
        "alpha.scada.telemetry.ingestion.max_deliveries_exhausted",
        description: "Telemetry messages that exhausted JetStream redelivery attempts");
    private static readonly Counter<long> UnknownTagsDropped = Meter.CreateCounter<long>(
        "alpha.scada.telemetry.ingestion.unknown_tags_dropped",
        description: "Telemetry samples dropped because their tags were not configured");
    private static readonly KeyValuePair<string, object?> ServiceTag = new("service", ServiceName);

    public TelemetryIngestionMeasurement Begin()
    {
        InFlight.Add(1, ServiceTag);
        return new TelemetryIngestionMeasurement(Stopwatch.GetTimestamp());
    }

    public void RecordMaxDeliveriesExhausted() =>
        MaxDeliveriesExhausted.Add(1, ServiceTag);

    public void RecordUnknownTagsDropped(int count)
    {
        if (count > 0)
        {
            UnknownTagsDropped.Add(count, ServiceTag);
        }
    }

    public Activity? StartIngestion(string subject, string messageId)
    {
        var activity = Activities.StartActivity("telemetry.ingest", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.destination.name", subject);
        activity?.SetTag("messaging.message.id", messageId);
        return activity;
    }

    public Activity? StartCatalogResolution(CanonicalTelemetry telemetry)
    {
        var activity = Activities.StartActivity("telemetry.catalog.resolve", ActivityKind.Client);
        activity?.SetTag("alpha.tenant.key", telemetry.TenantKey);
        activity?.SetTag("alpha.site.key", telemetry.SiteKey);
        activity?.SetTag("alpha.unit.key", telemetry.UnitKey);
        activity?.SetTag("alpha.telemetry.sample.count", telemetry.Readings.Count);
        return activity;
    }

    private static void Complete(long startedTimestamp, TelemetryIngestionOutcome outcome)
    {
        InFlight.Add(-1, ServiceTag);
        Messages.Add(
            1,
            ServiceTag,
            new KeyValuePair<string, object?>("outcome", OutcomeName(outcome)));
        ProcessingDuration.Record(
            Stopwatch.GetElapsedTime(startedTimestamp).TotalSeconds,
            ServiceTag);
    }

    private static string OutcomeName(TelemetryIngestionOutcome outcome) =>
        outcome switch
        {
            TelemetryIngestionOutcome.Success => "success",
            TelemetryIngestionOutcome.Dropped => "dropped",
            TelemetryIngestionOutcome.DeadLetter => "dead_letter",
            TelemetryIngestionOutcome.Retry => "retry",
            TelemetryIngestionOutcome.TerminalError => "terminal_error",
            TelemetryIngestionOutcome.EscapedError => "escaped_error",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported telemetry ingestion outcome.")
        };

    public readonly struct TelemetryIngestionMeasurement(long startedTimestamp)
    {
        public void Complete(TelemetryIngestionOutcome outcome) =>
            TelemetryIngestionMetrics.Complete(startedTimestamp, outcome);
    }
}
