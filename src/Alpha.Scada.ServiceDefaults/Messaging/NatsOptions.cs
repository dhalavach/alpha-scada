using Microsoft.Extensions.Configuration;

namespace Alpha.Scada.ServiceDefaults.Messaging;

public sealed class NatsOptions
{
    public const string SectionName = "Nats";

    public string Url { get; init; } = string.Empty;

    public string? User { get; init; }

    public string? Password { get; init; }

    public static NatsOptions FromConfiguration(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<NatsOptions>() ?? new NatsOptions();
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new InvalidOperationException($"{SectionName}:Url is required.");
        }

        return options;
    }
}
