using System.Text.Json;
using System.Reflection;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet.Client;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.MQTT;
using Wolverine.Postgresql;

namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class AlphaMessaging
{
    public static IHostBuilder UseAlphaMessaging(
        this IHostBuilder host,
        string serviceName,
        Action<WolverineOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        IConfiguration? configuration = null;
        host.ConfigureServices((context, _) => configuration = context.Configuration);

        return host.UseWolverine(options =>
        {
            Configure(options, configuration ?? BuildFallbackConfiguration(), serviceName, configure);
        });
    }

    private static void Configure(
        WolverineOptions options,
        IConfiguration configuration,
        string serviceName,
        Action<WolverineOptions>? configure)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required to enable Alpha messaging.");
        var storageSchema = configuration.GetValue("Wolverine:StorageSchema", "wolverine");
        var transportSchema = configuration.GetValue("Wolverine:TransportSchema", "wolverine_queues");

        options.ServiceName = serviceName;
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            options.ApplicationAssembly = entryAssembly;
            options.Discovery.IncludeAssembly(entryAssembly);
        }

        options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
        options.UseSystemTextJsonForSerialization(json =>
        {
            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            json.PropertyNamingPolicy = web.PropertyNamingPolicy;
            json.DictionaryKeyPolicy = web.DictionaryKeyPolicy;
            json.PropertyNameCaseInsensitive = web.PropertyNameCaseInsensitive;
            json.DefaultIgnoreCondition = web.DefaultIgnoreCondition;
            json.NumberHandling = web.NumberHandling;
        });

        options.PersistMessagesWithPostgresql(postgres, storageSchema)
            .EnableMessageTransport(postgresql => postgresql.TransportSchemaName(transportSchema));
        if (configuration.GetValue("Mqtt:Enabled", true))
        {
            ConfigureMqtt(options, configuration, serviceName);
        }

        options.Policies.AutoApplyTransactions();
        options.Policies.UseDurableInboxOnAllListeners();
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        options.OnAnyException()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30))
            .Then.MoveToErrorQueue();

        configure?.Invoke(options);
    }

    private static void ConfigureMqtt(WolverineOptions options, IConfiguration configuration, string serviceName)
    {
        var host = configuration.GetValue("Mqtt:Host", "localhost");
        var port = configuration.GetValue("Mqtt:Port", 1883);
        var user = configuration["Mqtt:User"];
        var password = configuration["Mqtt:Password"];
        var clientId = $"{serviceName}-{Environment.MachineName}-{Guid.NewGuid():N}";

        options.UseMqtt(mqtt =>
        {
            mqtt.WithAutoReconnectDelay(TimeSpan.FromSeconds(5));
            mqtt.WithClientOptions(client =>
            {
                client.WithClientId(clientId)
                    .WithTcpServer(host, port);

                if (!string.IsNullOrWhiteSpace(user))
                {
                    client.WithCredentials(user, password ?? string.Empty);
                }
            });
        });
    }

    private static IConfiguration BuildFallbackConfiguration() =>
        new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
}
