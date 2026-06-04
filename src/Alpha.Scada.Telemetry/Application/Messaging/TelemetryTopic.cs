namespace Alpha.Scada.Telemetry.Application.Messaging;

public sealed record TelemetryTopic(string TenantKey, string SiteKey, string UnitKey);

public static class TelemetryTopicParser
{
    public static TelemetryTopic? Parse(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return null;
        }

        var parts = topic.Split('.', StringSplitOptions.None);
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

    private static bool IsInvalidRouteKey(string value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains('.', StringComparison.Ordinal)
        || value.Contains('/', StringComparison.Ordinal);
}
