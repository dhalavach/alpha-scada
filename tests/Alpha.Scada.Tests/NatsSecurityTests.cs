using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using NATS.Client.Core;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class NatsSecurityTests
{
    [Fact]
    public async Task Nats_rejects_bad_mqtt_credentials_and_accepts_edge_ingress()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-nats-security-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "nats-server.conf"), """
                port: 4222
                jetstream {
                  store_dir: "/data/jetstream"
                }
                mqtt {
                  port: 1883
                }
                authorization {
                  users = [
                    { user: "edge", password: "edge-pass" },
                    { user: "services", password: "services-pass" }
                  ]
                }
                """);

            var nats = new ContainerBuilder()
                .WithImage("nats:2.12-alpine")
                .WithBindMount(tempDir, "/etc/nats", AccessMode.ReadOnly)
                .WithPortBinding(4222, true)
                .WithPortBinding(1883, true)
                .WithCommand("-c", "/etc/nats/nats-server.conf")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
                .Build();

            try
            {
                await nats.StartAsync();
            }
            catch (DockerUnavailableException ex)
            {
                throw SkipException.ForSkip($"Docker is not available for NATS integration test: {ex.Message}");
            }

            await using (nats)
            {
                Assert.False(await CanMqttConnectAsync(nats.Hostname, nats.GetMappedPublicPort(1883), "wrong", "wrong"));

                var received = WaitForSubjectAsync(
                    $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}",
                    "alpha.demo.site.unit.telemetry");

                var factory = new MqttFactory();
                using var edge = factory.CreateMqttClient();
                await edge.ConnectAsync(Options(nats.Hostname, nats.GetMappedPublicPort(1883), "edge", "edge-pass"));
                await Task.Delay(250);
                await edge.PublishAsync(Message("alpha/demo/site/unit/telemetry"));

                Assert.Equal("alpha.demo.site.unit.telemetry", await received.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<string> WaitForSubjectAsync(string natsUrl, string subject)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true,
            AuthOpts = new NatsAuthOpts
            {
                Username = "services",
                Password = "services-pass"
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var message in connection.SubscribeAsync<byte[]>(subject, cancellationToken: cts.Token))
        {
            return message.Subject;
        }

        throw new TimeoutException($"No NATS message arrived on {subject}.");
    }

    private static async Task<bool> CanMqttConnectAsync(string host, int port, string user, string password)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        try
        {
            await client.ConnectAsync(Options(host, port, user, password));
            return client.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    private static MqttClientOptions Options(string host, int port, string user, string password) =>
        new MqttClientOptionsBuilder()
            .WithClientId($"alpha-test-{Guid.NewGuid():N}")
            .WithTcpServer(host, port)
            .WithCredentials(user, password)
            .Build();

    private static MqttApplicationMessage Message(string topic) =>
        new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes("""{"schemaVersion":"1.0","samples":[]}"""))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
}
