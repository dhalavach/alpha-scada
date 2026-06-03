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

        var parts = topic.Contains('/', StringComparison.Ordinal)
            ? topic.Split('/', StringSplitOptions.RemoveEmptyEntries)
            : topic.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "alpha" || parts[4] != "telemetry")
        {
            return null;
        }

        return new TelemetryTopic(parts[1], parts[2], parts[3]);
    }
}
