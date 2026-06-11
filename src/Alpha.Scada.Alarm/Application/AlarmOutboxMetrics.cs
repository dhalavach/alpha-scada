using System.Globalization;
using System.Text;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmOutboxMetrics : IAlphaMetricsProvider
{
    private long pending;
    private long poison;

    public void Update(long pendingCount, long poisonCount)
    {
        Interlocked.Exchange(ref pending, pendingCount);
        Interlocked.Exchange(ref poison, poisonCount);
    }

    public void AppendMetrics(StringBuilder metrics, string serviceName)
    {
        var serviceLabel = PrometheusLabels.Escape(serviceName);
        metrics.AppendLine("# HELP alpha_scada_alarm_outbox_pending Alarm outbox rows awaiting successful delivery");
        metrics.AppendLine("# TYPE alpha_scada_alarm_outbox_pending gauge");
        metrics.AppendLine(
            CultureInfo.InvariantCulture,
            $"alpha_scada_alarm_outbox_pending{{service=\"{serviceLabel}\"}} {Interlocked.Read(ref pending)}");
        metrics.AppendLine("# HELP alpha_scada_alarm_outbox_poison_total Alarm outbox rows that exhausted delivery attempts");
        metrics.AppendLine("# TYPE alpha_scada_alarm_outbox_poison_total gauge");
        metrics.AppendLine(
            CultureInfo.InvariantCulture,
            $"alpha_scada_alarm_outbox_poison_total{{service=\"{serviceLabel}\"}} {Interlocked.Read(ref poison)}");
    }
}
