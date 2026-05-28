using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
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

public sealed class StatusBroadcastTests
{
    [Fact]
    public async Task Unit_status_changed_is_published_to_unit_status_topic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-status-broadcast-test-{Guid.NewGuid():N}");
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
                throw SkipException.ForSkip($"Docker is not available for status broadcast integration test: {ex.Message}");
            }

            await using (postgres)
            await using (mosquitto)
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
                        .WithClientId($"status-test-subscriber-{Guid.NewGuid():N}")
                        .Build());
                await subscriber.SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter("alpha/demo-operator/demo-site/chp-001/status", MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
                        .Build());

                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = connectionString,
                    ["Mqtt:Host"] = mosquitto.Hostname,
                    ["Mqtt:Port"] = mosquitto.GetMappedPublicPort(1883).ToString()
                };

                using var host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
                    .UseAlphaMessaging("status-test", options =>
                    {
                        options.PublishMessagesToMqttTopic<UnitStatusChanged>(message =>
                            Topics.Status(message.TenantKey, message.SiteKey, message.UnitKey));
                    })
                    .Build();

                await host.StartAsync();
                await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(new UnitStatusChanged(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "demo-operator",
                    "demo-site",
                    "chp-001",
                    "CHP 001",
                    "online",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));

                Assert.Equal(
                    "alpha/demo-operator/demo-site/chp-001/status",
                    await receivedTopic.Task.WaitAsync(TimeSpan.FromSeconds(10)));

                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
}
