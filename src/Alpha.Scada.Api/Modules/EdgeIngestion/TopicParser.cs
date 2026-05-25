namespace Alpha.Scada.Api.Modules.EdgeIngestion;

public sealed record EdgeTopic(string TenantKey, string SiteKey, string UnitKey, string Kind);

public static class TopicParser
{
    public static EdgeTopic? Parse(string topic)
    {
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "alpha")
        {
            return null;
        }

        if (parts[4] is not ("telemetry" or "status"))
        {
            return null;
        }

        return new EdgeTopic(parts[1], parts[2], parts[3], parts[4]);
    }
}
