using System.Text;
using Alpha.Scada.Telemetry.Application;

namespace Alpha.Scada.Tests;

public sealed class TelemetryIngestionMetricsTests
{
    [Fact]
    public void Metrics_render_in_flight_outcomes_and_duration_histogram()
    {
        var metrics = new TelemetryIngestionMetrics();
        var measurement = metrics.Begin();
        measurement.Complete(TelemetryIngestionOutcome.Success);

        var text = new StringBuilder();
        metrics.AppendMetrics(text, "alpha-scada-telemetry");
        var rendered = text.ToString();

        Assert.Contains("alpha_scada_telemetry_ingestion_in_flight", rendered);
        Assert.Contains("outcome=\"success\"} 1", rendered);
        Assert.Contains("alpha_scada_telemetry_ingestion_processing_seconds_count", rendered);
        Assert.Contains("alpha_scada_telemetry_ingestion_processing_seconds_sum", rendered);
    }
}
