/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/ServiceIdentity.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: File-scoped namespaces and small sealed classes/records are modern C# style choices that reduce boilerplate and make ownership explicit.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Diagnostics;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class ServiceIdentity
{
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static Guid NewMessageId() => Guid.NewGuid();

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static Guid? CurrentCorrelationId() =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        Activity.Current is { } activity ? ToGuid(activity.TraceId) : null;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static Guid? CurrentCausationId()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var activity = Activity.Current;
// LEARN: returns a value or exits the current method.
        return activity is null ? null : ToGuid(activity.TraceId, activity.SpanId);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static Guid ToGuid(ActivityTraceId traceId)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        Span<byte> bytes = stackalloc byte[16];
// LEARN: executes one C# statement; semicolons terminate most statements.
        traceId.CopyTo(bytes);
// LEARN: returns a value or exits the current method.
        return new Guid(bytes);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static Guid ToGuid(ActivityTraceId traceId, ActivitySpanId spanId)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        Span<byte> bytes = stackalloc byte[16];
// LEARN: executes one C# statement; semicolons terminate most statements.
        traceId.CopyTo(bytes);
// LEARN: executes one C# statement; semicolons terminate most statements.
        spanId.CopyTo(bytes[8..]);
// LEARN: returns a value or exits the current method.
        return new Guid(bytes);
    }
}
