using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NATS.Client.Core;
using NATS.Client.JetStream;

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
            .WithImage(TestImages.Nats)
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

    public static async Task PublishAsync(
        string natsUrl,
        string subject,
        byte[] payload,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true
        });
        var headers = new NatsHeaders
        {
            ["Nats-Msg-Id"] = messageId
        };
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        await jetStream.PublishAsync(subject, payload, headers: headers, cancellationToken: cancellationToken);
    }

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

        using var cts = new CancellationTokenSource();
        var receive = Task.Run(async () =>
        {
            await foreach (var message in connection.SubscribeAsync<string>(subject, cancellationToken: cts.Token)
                               .WithCancellation(cts.Token))
            {
                return message.Subject;
            }

            throw new TimeoutException($"No NATS message arrived on {subject}.");
        }, CancellationToken.None);

        try
        {
            return await receive.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            await cts.CancelAsync();
            throw new TimeoutException($"No NATS message arrived on {subject} within {timeout}.");
        }
    }
}
