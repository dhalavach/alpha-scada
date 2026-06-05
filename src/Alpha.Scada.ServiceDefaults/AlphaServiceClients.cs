/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/AlphaServiceClients.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Options;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlphaServiceClients
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Identity = "identity";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Tenant = "tenant";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Asset = "asset";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string TagCatalog = "tagCatalog";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Telemetry = "telemetry";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Alarm = "alarm";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Reporting = "reporting";

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static IServiceCollection AddAlphaServiceClients(
// LEARN: continues an argument/object/collection initializer onto the next line.
        this IServiceCollection services,
// LEARN: continues an argument/object/collection initializer onto the next line.
        IConfiguration configuration,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        params string[] clientNames)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var options = AlphaServiceEndpointOptions.FromConfiguration(configuration);
// LEARN: loops over each item in a collection.
        foreach (var clientName in clientNames.Distinct(StringComparer.Ordinal))
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            _ = options.GetRequiredEndpoint(clientName);
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            services.AddHttpClient(clientName, (provider, client) =>
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var endpoints = provider.GetRequiredService<IOptions<AlphaServiceEndpointOptions>>().Value;
// LEARN: executes one C# statement; semicolons terminate most statements.
                client.BaseAddress = endpoints.GetRequiredEndpoint(clientName);
// LEARN: executes one C# statement; semicolons terminate most statements.
            }).AddAlphaResilience();
        }

// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<IOptions<AlphaServiceEndpointOptions>>(Options.Create(options));
// LEARN: returns a value or exits the current method.
        return services;
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static string ConfigurationKey(string clientName) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        clientName switch
        {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Identity => nameof(AlphaServiceEndpointOptions.Identity),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Tenant => nameof(AlphaServiceEndpointOptions.Tenant),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Asset => nameof(AlphaServiceEndpointOptions.Asset),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            TagCatalog => nameof(AlphaServiceEndpointOptions.TagCatalog),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Telemetry => nameof(AlphaServiceEndpointOptions.Telemetry),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Alarm => nameof(AlphaServiceEndpointOptions.Alarm),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            Reporting => nameof(AlphaServiceEndpointOptions.Reporting),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            _ => throw new ArgumentOutOfRangeException(nameof(clientName), clientName, "Unknown Alpha service client.")
        };
}

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlphaServiceEndpointOptions
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string SectionName = "Services";

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? Identity { get; init; }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? Tenant { get; init; }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? Asset { get; init; }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? TagCatalog { get; init; }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? Telemetry { get; init; }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? Alarm { get; init; }

// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
    public string? Reporting { get; init; }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static AlphaServiceEndpointOptions FromConfiguration(IConfiguration configuration) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new()
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Identity = configuration[$"{SectionName}:{nameof(Identity)}"],
// LEARN: continues an argument/object/collection initializer onto the next line.
            Tenant = configuration[$"{SectionName}:{nameof(Tenant)}"],
// LEARN: continues an argument/object/collection initializer onto the next line.
            Asset = configuration[$"{SectionName}:{nameof(Asset)}"],
// LEARN: continues an argument/object/collection initializer onto the next line.
            TagCatalog = configuration[$"{SectionName}:{nameof(TagCatalog)}"],
// LEARN: continues an argument/object/collection initializer onto the next line.
            Telemetry = configuration[$"{SectionName}:{nameof(Telemetry)}"],
// LEARN: continues an argument/object/collection initializer onto the next line.
            Alarm = configuration[$"{SectionName}:{nameof(Alarm)}"],
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            Reporting = configuration[$"{SectionName}:{nameof(Reporting)}"]
        };

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public Uri GetRequiredEndpoint(string clientName)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var key = AlphaServiceClients.ConfigurationKey(clientName);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var value = key switch
        {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            nameof(Identity) => Identity,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            nameof(Tenant) => Tenant,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            nameof(Asset) => Asset,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            nameof(TagCatalog) => TagCatalog,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            nameof(Telemetry) => Telemetry,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            nameof(Alarm) => Alarm,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            nameof(Reporting) => Reporting,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            _ => null
        };

// LEARN: branches only when the boolean condition is true.
        if (string.IsNullOrWhiteSpace(value))
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException($"{SectionName}:{key} must be configured for service client '{clientName}'.");
        }

// LEARN: branches only when the boolean condition is true.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw new InvalidOperationException($"{SectionName}:{key} must be an absolute URI. Current value: '{value}'.");
        }

// LEARN: returns a value or exits the current method.
        return endpoint;
    }
}
