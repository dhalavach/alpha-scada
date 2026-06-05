/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/Messaging/TelemetryTopic.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

namespace Alpha.Scada.Telemetry.Application.Messaging;

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record TelemetryTopic(string TenantKey, string SiteKey, string UnitKey);

// LEARN: declares a static helper class whose members are called on the type itself.
public static class TelemetryTopicParser
{
    public static TelemetryTopic? Parse(string? topic)
    {
// LEARN: branches only when the boolean condition is true.
        if (string.IsNullOrWhiteSpace(topic))
        {
            return null;
        }

        var parts = topic.Split('.', StringSplitOptions.None);
// LEARN: branches only when the boolean condition is true.
        if (parts.Length != 5
            || parts[0] != "alpha"
            || parts[4] != "telemetry"
            || IsInvalidRouteKey(parts[1])
            || IsInvalidRouteKey(parts[2])
            || IsInvalidRouteKey(parts[3]))
        {
            return null;
        }

        return new TelemetryTopic(parts[1], parts[2], parts[3]);
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static bool IsInvalidRouteKey(string value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains('.', StringComparison.Ordinal)
        || value.Contains('/', StringComparison.Ordinal);
}
