using System.Diagnostics;
using System.Diagnostics.Metrics;
using Alpha.Scada.ServiceDefaults;

namespace Alpha.Scada.Alarm.Application;

public sealed class AlarmOutboxMetrics
{
    private const string ServiceName = "alpha-scada-alarm";
    private static readonly Meter Meter = new(AlphaObservability.AlarmMeterName);
    private static readonly ActivitySource Activities = new(AlphaObservability.AlarmMeterName);
    private static readonly KeyValuePair<string, object?> ServiceTag = new("service", ServiceName);
    private readonly ObservableGauge<long> pendingGauge;
    private readonly ObservableGauge<long> poisonGauge;
    private long pending;
    private long poison;

    public AlarmOutboxMetrics()
    {
        pendingGauge = Meter.CreateObservableGauge(
            "alpha.scada.alarm.outbox.pending",
            () => new Measurement<long>(Volatile.Read(ref pending), ServiceTag),
            description: "Alarm outbox rows awaiting successful delivery");
        poisonGauge = Meter.CreateObservableGauge(
            "alpha.scada.alarm.outbox.poison",
            () => new Measurement<long>(Volatile.Read(ref poison), ServiceTag),
            description: "Alarm outbox rows that exhausted delivery attempts");
    }

    public void Update(long pendingCount, long poisonCount)
    {
        Interlocked.Exchange(ref pending, pendingCount);
        Interlocked.Exchange(ref poison, poisonCount);
    }

    public long PendingCount => Volatile.Read(ref pending);

    public long PoisonCount => Volatile.Read(ref poison);

    public Activity? StartDispatch(Guid outboxId, string eventType)
    {
        var activity = Activities.StartActivity("alarm.outbox.dispatch", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.message.id", outboxId);
        activity?.SetTag("messaging.message.type", eventType);
        return activity;
    }
}
