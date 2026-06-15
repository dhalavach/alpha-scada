using Alpha.Scada.Telemetry.Application;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Alpha.Scada.Tests;

public sealed class TelemetryIngestionMetricsTests
{
    [Fact]
    public void Metrics_record_outcomes_duration_and_unknown_tags()
    {
        using var messages = new MetricCollector<long>(
            null,
            "Alpha.Scada.Telemetry",
            "alpha.scada.telemetry.ingestion.messages");
        using var duration = new MetricCollector<double>(
            null,
            "Alpha.Scada.Telemetry",
            "alpha.scada.telemetry.ingestion.processing");
        using var unknownTags = new MetricCollector<long>(
            null,
            "Alpha.Scada.Telemetry",
            "alpha.scada.telemetry.ingestion.unknown_tags_dropped");
        var metrics = new TelemetryIngestionMetrics();
        var measurement = metrics.Begin();
        measurement.Complete(TelemetryIngestionOutcome.Success);
        metrics.RecordUnknownTagsDropped(2);

        var outcome = Assert.Single(messages.GetMeasurementSnapshot());
        Assert.Equal(1, outcome.Value);
        Assert.Contains(outcome.Tags, tag => tag.Key == "outcome" && Equals(tag.Value, "success"));
        Assert.Single(duration.GetMeasurementSnapshot());
        Assert.Equal(2, Assert.Single(unknownTags.GetMeasurementSnapshot()).Value);
    }
}
