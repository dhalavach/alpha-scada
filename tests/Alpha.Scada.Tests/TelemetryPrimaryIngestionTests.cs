using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Npgsql;
using Wolverine.MQTT;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class TelemetryPrimaryIngestionTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Raw_mqtt_telemetry_is_normalized_into_primary_storage_and_published_as_domain_event()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-telemetry-primary-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "mosquitto.conf"), """
                listener 1883
                allow_anonymous true
                persistence false
                log_dest stdout
                """);

            var postgres = new ContainerBuilder()
                .WithImage("postgres:16-alpine")
                .WithEnvironment("POSTGRES_DB", "alpha_test")
                .WithEnvironment("POSTGRES_USER", "alpha")
                .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
                .WithPortBinding(5432, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
                .Build();

            var mosquitto = new ContainerBuilder()
                .WithImage("eclipse-mosquitto:2")
                .WithBindMount(tempDir, "/mosquitto/config", AccessMode.ReadOnly)
                .WithPortBinding(1883, true)
                .WithCommand("mosquitto", "-c", "/mosquitto/config/mosquitto.conf")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883))
                .Build();

            try
            {
                await postgres.StartAsync();
                await mosquitto.StartAsync();
            }
            catch (DockerUnavailableException ex)
            {
                await postgres.DisposeAsync();
                await mosquitto.DisposeAsync();
                throw SkipException.ForSkip($"Docker is not available for telemetry primary integration test: {ex.Message}");
            }

            await using (postgres)
            await using (mosquitto)
            await using (var catalog = await FakeCatalogServer.StartAsync())
            {
                var connectionString =
                    $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
                await WaitForPostgresAsync(connectionString);

                var factory = new MqttFactory();
                using var subscriber = factory.CreateMqttClient();
                var receivedTopic = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                subscriber.ApplicationMessageReceivedAsync += args =>
                {
                    receivedTopic.TrySetResult(args.ApplicationMessage.Topic);
                    return Task.CompletedTask;
                };

                await subscriber.ConnectAsync(new MqttClientOptionsBuilder()
                    .WithClientId($"telemetry-primary-subscriber-{Guid.NewGuid():N}")
                    .WithTcpServer(mosquitto.Hostname, mosquitto.GetMappedPublicPort(1883))
                    .Build());
                await subscriber.SubscribeAsync(Subscribe(Topics.TelemetryStoredWildcard));

                using var host = BuildTelemetryHost(
                    connectionString,
                    mosquitto.Hostname,
                    mosquitto.GetMappedPublicPort(1883),
                    catalog.BaseAddress);

                await host.Services.GetRequiredService<TelemetryMigrator>().MigrateAsync(CancellationToken.None);
                await host.StartAsync();

                using var publisher = factory.CreateMqttClient();
                await publisher.ConnectAsync(new MqttClientOptionsBuilder()
                    .WithClientId($"telemetry-primary-publisher-{Guid.NewGuid():N}")
                    .WithTcpServer(mosquitto.Hostname, mosquitto.GetMappedPublicPort(1883))
                    .Build());

                var timestamp = DateTimeOffset.UtcNow;
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    new TelemetryEnvelopeV1(
                        TelemetryEnvelopeV1.SchemaVersion,
                        "chp-demo-001",
                        timestamp,
                        [new("engine.electrical_output_kw", 61.2, "good", timestamp)]),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

                await publisher.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic("alpha/demo-operator/demo-energy-site/chp-demo-001/telemetry")
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());

                Assert.Equal(
                    "alpha/demo-operator/demo-energy-site/chp-demo-001/telemetry-stored",
                    await receivedTopic.Task.WaitAsync(TimeSpan.FromSeconds(10)));

                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
                Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));

                await host.Services.GetRequiredService<TelemetryRepository>().IngestAsync(
                    new TelemetryIngestRequest(
                        TenantId,
                        UnitId,
                        [
                            new(
                                TagId,
                                "engine.electrical_output_kw",
                                "Electrical Output",
                                "Engine",
                                "kW",
                                45,
                                70,
                                61.2,
                                "good",
                                timestamp)
                        ]),
                    CancellationToken.None);

                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
                Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));

                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static IHost BuildTelemetryHost(string connectionString, string mqttHost, int mqttPort, string catalogBaseAddress)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Mqtt:Host"] = mqttHost,
            ["Mqtt:Port"] = mqttPort.ToString(),
            ["Services:Tenant"] = catalogBaseAddress,
            ["Services:Asset"] = catalogBaseAddress,
            ["Services:TagCatalog"] = catalogBaseAddress
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services.AddServiceDatabase(context.Configuration);
                services.AddSingleton<TelemetryMigrator>();
                services.AddSingleton<TelemetryRepository>();
                services.AddSingleton<CatalogCache>();
                services.AddMemoryCache();
                services.AddDomainOutbox();
                services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(catalogBaseAddress));
                services.AddHttpClient("asset", client => client.BaseAddress = new Uri(catalogBaseAddress));
                services.AddHttpClient("tagCatalog", client => client.BaseAddress = new Uri(catalogBaseAddress));
            })
            .UseAlphaMessaging("telemetry-primary-test", options =>
            {
                options.Discovery.IncludeAssembly(typeof(TelemetryIngestionHandler).Assembly);
                options.ListenToMqttTopic(Topics.TelemetryWildcard)
                    .UseInterop(new RawTelemetryEnvelopeMapper())
                    .DefaultIncomingMessage(typeof(TelemetryEnvelopeV1));
                options.PublishMessagesToMqttTopic<TelemetryBatchStored>(message =>
                    Topics.TelemetryStored(message.TenantKey, message.SiteKey, message.UnitKey));
            })
            .Build();
    }

    private static MqttClientSubscribeOptions Subscribe(string topic) =>
        new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topic, MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
            .Build();

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }

    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class FakeCatalogServer(WebApplication app) : IAsyncDisposable
    {
        public string BaseAddress { get; } = app.Urls.Single();

        public static async Task<FakeCatalogServer> StartAsync()
        {
            var port = GetFreePort();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build();

            app.MapGet("/internal/v1/tenants/resolve/{tenantKey}", (string tenantKey) =>
                Results.Ok(new TenantDto(TenantId, tenantKey, "Demo Operator", "EU")));

            app.MapGet("/internal/v1/units/resolve", (Guid tenantId, string siteKey, string unitKey) =>
                tenantId == TenantId && siteKey == "demo-energy-site" && unitKey == "chp-demo-001"
                    ? Results.Ok(new ResolvedUnitDto(TenantId, SiteId, UnitId, "Combined Heat and Power Unit 001", "online"))
                    : Results.NotFound());

            app.MapPost("/internal/v1/tags/resolve", (ResolveTagsRequest request) =>
                Results.Ok<IReadOnlyCollection<TagDto>>([
                    new(
                        TagId,
                        request.TenantId,
                        request.UnitId,
                        "engine.electrical_output_kw",
                        "Electrical Output",
                        "Engine",
                        "kW",
                        45,
                        70)
                ]));

            await app.StartAsync();
            return new FakeCatalogServer(app);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
