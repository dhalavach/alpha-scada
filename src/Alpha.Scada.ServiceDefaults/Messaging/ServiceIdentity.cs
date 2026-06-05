/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/ServiceIdentity.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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
