/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/NatsTestSupport.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Alpha.Scada.ServiceDefaults.Messaging;
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
            [RawTelemetryHeaders.NatsMessageId] = messageId
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
