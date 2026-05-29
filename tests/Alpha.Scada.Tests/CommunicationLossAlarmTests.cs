using System.Net;
using System.Net.Sockets;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
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
using Wolverine;
using Wolverine.MQTT;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class CommunicationLossAlarmTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Offline_unit_status_raises_communication_lost_alarm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-comm-loss-test-{Guid.NewGuid():N}");
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
                throw SkipException.ForSkip($"Docker is not available for communication-loss integration test: {ex.Message}");
            }

            await using (postgres)
            await using (mosquitto)
            await using (var catalog = await FakeRouteServer.StartAsync())
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

                await subscriber.ConnectAsync(
                    new MqttClientOptionsBuilder()
                        .WithTcpServer(mosquitto.Hostname, mosquitto.GetMappedPublicPort(1883))
                        .WithClientId($"comm-loss-subscriber-{Guid.NewGuid():N}")
                        .Build());
                await subscriber.SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter("alpha/demo-operator/demo-site/chp-001/alarm/raised", MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
                        .Build());

                using var host = BuildAlarmHost(
                    connectionString,
                    mosquitto.Hostname,
                    mosquitto.GetMappedPublicPort(1883),
                    catalog.BaseAddress);

                await host.Services.GetRequiredService<AlarmMigrator>().MigrateAsync(CancellationToken.None);
                await host.StartAsync();

                await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(new UnitStatusChanged(
                    TenantId,
                    SiteId,
                    UnitId,
                    "demo-operator",
                    "demo-site",
                    "chp-001",
                    "Combined Heat and Power Unit 001",
                    "offline",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(5))));

                Assert.Equal(
                    "alpha/demo-operator/demo-site/chp-001/alarm/raised",
                    await receivedTopic.Task.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));

                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static IHost BuildAlarmHost(string connectionString, string mqttHost, int mqttPort, string routeBaseAddress)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Mqtt:Host"] = mqttHost,
            ["Mqtt:Port"] = mqttPort.ToString(),
            ["Services:Asset"] = routeBaseAddress,
            ["Services:Tenant"] = routeBaseAddress
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services.AddServiceDatabase(context.Configuration);
                services.AddSingleton<AlarmMigrator>();
                services.AddSingleton<AlarmRepository>();
                services.AddSingleton<AlarmService>();
                services.AddSingleton<UnitKeyResolver>();
                services.AddMemoryCache();
                services.AddHttpClient("asset", client => client.BaseAddress = new Uri(routeBaseAddress));
                services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(routeBaseAddress));
            })
            .UseAlphaMessaging("comm-loss-test", options =>
            {
                options.Discovery.IncludeAssembly(typeof(UnitStatusAlarmHandler).Assembly);
                options.ListenToMqttTopic(Topics.StatusWildcard);
                options.PublishMessagesToMqttTopic<UnitStatusChanged>(message =>
                    Topics.Status(message.TenantKey, message.SiteKey, message.UnitKey));
                options.PublishMessagesToMqttTopic<AlarmRaised>(message =>
                    Topics.AlarmRaised(message.TenantKey, message.SiteKey, message.UnitKey));
            })
            .Build();
    }

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

    private sealed class FakeRouteServer(WebApplication app) : IAsyncDisposable
    {
        public string BaseAddress { get; } = app.Urls.Single();

        public static async Task<FakeRouteServer> StartAsync()
        {
            var port = GetFreePort();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build();

            app.MapGet("/internal/v1/units/{unitId:guid}/route", (Guid unitId) =>
                unitId == UnitId
                    ? Results.Ok(new UnitRouteDto(TenantId, SiteId, UnitId, "demo-site", "chp-001", "Combined Heat and Power Unit 001"))
                    : Results.NotFound());

            app.MapGet("/internal/v1/tenants/{tenantId:guid}", (Guid tenantId) =>
                tenantId == TenantId
                    ? Results.Ok(new TenantDto(TenantId, "demo-operator", "Demo Operator", "EU"))
                    : Results.NotFound());

            await app.StartAsync();
            return new FakeRouteServer(app);
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
