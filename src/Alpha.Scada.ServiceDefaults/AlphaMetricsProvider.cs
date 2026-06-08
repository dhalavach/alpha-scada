using System.Text;

namespace Alpha.Scada.ServiceDefaults;

public interface IAlphaMetricsProvider
{
    void AppendMetrics(StringBuilder metrics, string serviceName);
}
