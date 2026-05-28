using System.Diagnostics;

namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class ServiceIdentity
{
    public static Guid NewMessageId() => Guid.NewGuid();

    public static Guid? CurrentCorrelationId() =>
        Activity.Current is { } activity ? ToGuid(activity.TraceId) : null;

    public static Guid? CurrentCausationId()
    {
        var activity = Activity.Current;
        return activity is null ? null : ToGuid(activity.TraceId, activity.SpanId);
    }

    private static Guid ToGuid(ActivityTraceId traceId)
    {
        Span<byte> bytes = stackalloc byte[16];
        traceId.CopyTo(bytes);
        return new Guid(bytes);
    }

    private static Guid ToGuid(ActivityTraceId traceId, ActivitySpanId spanId)
    {
        Span<byte> bytes = stackalloc byte[16];
        traceId.CopyTo(bytes);
        spanId.CopyTo(bytes[8..]);
        return new Guid(bytes);
    }
}
