using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NATS.Client.Core;

namespace Alpha.Scada.Tests;

internal static class NatsTestSupport
{
    public static async Task<IContainer> StartAsync(string tempDir)
    {
        await File.WriteAllTextAsync(Path.Combine(tempDir, "nats-server.conf"), """
            port: 4222
            http_port: 8222
            jetstream {
              store_dir: "/data/jetstream"
            }
            mqtt {
              port: 1883
            }
            """);

        var nats = new ContainerBuilder()
            .WithImage("nats:2.12-alpine")
            .WithBindMount(tempDir, "/etc/nats", AccessMode.ReadOnly)
            .WithPortBinding(4222, true)
            .WithPortBinding(1883, true)
            .WithPortBinding(8222, true)
            .WithCommand("-c", "/etc/nats/nats-server.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
            .Build();

        await nats.StartAsync();
        return nats;
    }

    public static string Url(IContainer nats) =>
        $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";

    public static int MqttPort(IContainer nats) =>
        nats.GetMappedPublicPort(1883);

    public static async Task<string> WaitForSubjectAsync(
        string natsUrl,
        string subject,
        TimeSpan timeout)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true
        });

        using var cts = new CancellationTokenSource(timeout);
        await foreach (var message in connection.SubscribeAsync<string>(subject, cancellationToken: cts.Token))
        {
            return message.Subject;
        }

        throw new TimeoutException($"No NATS message arrived on {subject} within {timeout}.");
    }
}
