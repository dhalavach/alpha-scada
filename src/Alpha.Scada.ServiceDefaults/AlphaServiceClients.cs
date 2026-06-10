using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Alpha.Scada.ServiceDefaults;

public static class AlphaServiceClients
{
    public const string Identity = "identity";
    public const string Tenant = "tenant";
    public const string Asset = "asset";
    public const string TagCatalog = "tagCatalog";
    public const string Telemetry = "telemetry";
    public const string Alarm = "alarm";
    public const string Reporting = "reporting";

    public static IServiceCollection AddAlphaServiceClients(
        this IServiceCollection services,
        IConfiguration configuration,
        params string[] clientNames)
    {
        var options = AlphaServiceEndpointOptions.FromConfiguration(configuration);
        foreach (var clientName in clientNames.Distinct(StringComparer.Ordinal))
        {
            _ = options.GetRequiredEndpoint(clientName);
        }

        services.AddJwtTokenService(configuration);
        services.TryAddSingleton<ServiceTokenProvider>();
        services.TryAddTransient<ServiceAuthorizationHandler>();
        services.AddSingleton<IOptions<AlphaServiceEndpointOptions>>(Options.Create(options));
        foreach (var clientName in clientNames.Distinct(StringComparer.Ordinal))
        {
            services.AddHttpClient(clientName, (provider, client) =>
            {
                var endpoints = provider.GetRequiredService<IOptions<AlphaServiceEndpointOptions>>().Value;
                client.BaseAddress = endpoints.GetRequiredEndpoint(clientName);
            })
                .AddHttpMessageHandler<ServiceAuthorizationHandler>()
                .AddAlphaResilience();
        }

        return services;
    }

    public static string ConfigurationKey(string clientName) =>
        clientName switch
        {
            Identity => nameof(AlphaServiceEndpointOptions.Identity),
            Tenant => nameof(AlphaServiceEndpointOptions.Tenant),
            Asset => nameof(AlphaServiceEndpointOptions.Asset),
            TagCatalog => nameof(AlphaServiceEndpointOptions.TagCatalog),
            Telemetry => nameof(AlphaServiceEndpointOptions.Telemetry),
            Alarm => nameof(AlphaServiceEndpointOptions.Alarm),
            Reporting => nameof(AlphaServiceEndpointOptions.Reporting),
            _ => throw new ArgumentOutOfRangeException(nameof(clientName), clientName, "Unknown Alpha service client.")
        };
}

public sealed class AlphaServiceEndpointOptions
{
    public const string SectionName = "Services";

    public string? Identity { get; init; }

    public string? Tenant { get; init; }

    public string? Asset { get; init; }

    public string? TagCatalog { get; init; }

    public string? Telemetry { get; init; }

    public string? Alarm { get; init; }

    public string? Reporting { get; init; }

    public static AlphaServiceEndpointOptions FromConfiguration(IConfiguration configuration) =>
        new()
        {
            Identity = configuration[$"{SectionName}:{nameof(Identity)}"],
            Tenant = configuration[$"{SectionName}:{nameof(Tenant)}"],
            Asset = configuration[$"{SectionName}:{nameof(Asset)}"],
            TagCatalog = configuration[$"{SectionName}:{nameof(TagCatalog)}"],
            Telemetry = configuration[$"{SectionName}:{nameof(Telemetry)}"],
            Alarm = configuration[$"{SectionName}:{nameof(Alarm)}"],
            Reporting = configuration[$"{SectionName}:{nameof(Reporting)}"]
        };

    public Uri GetRequiredEndpoint(string clientName)
    {
        var key = AlphaServiceClients.ConfigurationKey(clientName);
        var value = key switch
        {
            nameof(Identity) => Identity,
            nameof(Tenant) => Tenant,
            nameof(Asset) => Asset,
            nameof(TagCatalog) => TagCatalog,
            nameof(Telemetry) => Telemetry,
            nameof(Alarm) => Alarm,
            nameof(Reporting) => Reporting,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{SectionName}:{key} must be configured for service client '{clientName}'.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException($"{SectionName}:{key} must be an absolute URI. Current value: '{value}'.");
        }

        return endpoint;
    }
}
