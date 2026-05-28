using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using MQTTnet;
using MQTTnet.Protocol;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class MosquittoSecurityTests
{
    [Fact]
    public async Task Mosquitto_rejects_bad_credentials_and_enforces_edge_acl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-mosquitto-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "mosquitto.conf"), """
                listener 1883
                allow_anonymous false
                password_file /tmp/passwords
                acl_file /mosquitto/config/acl
                persistence false
                log_dest stdout
                """);
            File.Copy(Path.Combine(ProjectRoot(), "ops", "mosquitto", "acl"), Path.Combine(tempDir, "acl"));

            var container = new ContainerBuilder()
                .WithImage("eclipse-mosquitto:2")
                .WithBindMount(tempDir, "/mosquitto/config", AccessMode.ReadOnly)
                .WithPortBinding(1883, true)
                .WithCommand("sh", "-c", string.Join(" && ", [
                    "mosquitto_passwd -b -c /tmp/passwords edge edge-pass",
                    "mosquitto_passwd -b /tmp/passwords edge-ingestor ingest-pass",
                    "chmod 644 /tmp/passwords",
                    "mosquitto -c /mosquitto/config/mosquitto.conf"
                ]))
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883))
                .Build();

            try
            {
                await container.StartAsync();
            }
            catch (DockerUnavailableException ex)
            {
                throw SkipException.ForSkip($"Docker is not available for Mosquitto integration test: {ex.Message}");
            }

            await using (container)
            {
                var host = container.Hostname;
                var port = container.GetMappedPublicPort(1883);

                Assert.False(await CanConnectAsync(host, port, "wrong", "wrong"));
                await AssertIngestorCanReadTelemetryAsync(host, port);
                await AssertEdgeCannotReadTelemetryAsync(host, port);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<bool> CanConnectAsync(string host, int port, string user, string password)
    {
        var factory = new MqttClientFactory();
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

    private static async Task AssertIngestorCanReadTelemetryAsync(string host, int port)
    {
        var factory = new MqttClientFactory();
        using var reader = factory.CreateMqttClient();
        using var writer = factory.CreateMqttClient();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.ApplicationMessageReceivedAsync += args =>
        {
            received.TrySetResult(args.ApplicationMessage.Topic);
            return Task.CompletedTask;
        };

        await reader.ConnectAsync(Options(host, port, "edge-ingestor", "ingest-pass"));
        await reader.SubscribeAsync(Subscribe("alpha/+/+/+/telemetry"));
        await writer.ConnectAsync(Options(host, port, "edge", "edge-pass"));
        await writer.PublishAsync(Message("alpha/demo/site/unit/telemetry"));

        Assert.Equal("alpha/demo/site/unit/telemetry", await received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static async Task AssertEdgeCannotReadTelemetryAsync(string host, int port)
    {
        var factory = new MqttClientFactory();
        using var reader = factory.CreateMqttClient();
        using var writer = factory.CreateMqttClient();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.ApplicationMessageReceivedAsync += args =>
        {
            received.TrySetResult(args.ApplicationMessage.Topic);
            return Task.CompletedTask;
        };

        await reader.ConnectAsync(Options(host, port, "edge", "edge-pass"));
        await reader.SubscribeAsync(Subscribe("alpha/#"));
        await writer.ConnectAsync(Options(host, port, "edge", "edge-pass"));
        await writer.PublishAsync(Message("alpha/demo/site/unit/telemetry"));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.False(received.Task.IsCompleted);
    }

    private static MqttClientOptions Options(string host, int port, string user, string password)
    {
        return new MqttClientOptionsBuilder()
            .WithClientId($"alpha-test-{Guid.NewGuid():N}")
            .WithTcpServer(host, port)
            .WithCredentials(user, password)
            .Build();
    }

    private static MqttClientSubscribeOptions Subscribe(string topic)
    {
        return new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topic, MqttQualityOfServiceLevel.AtLeastOnce, false, false, MqttRetainHandling.SendAtSubscribe)
            .Build();
    }

    private static MqttApplicationMessage Message(string topic)
    {
        return new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes("""{"schemaVersion":"1.0","samples":[]}"""))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
    }

    private static string ProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current, "Alpha.Scada.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
