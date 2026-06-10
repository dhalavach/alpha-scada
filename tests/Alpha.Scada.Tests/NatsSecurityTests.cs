using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NATS.Client.Core;

namespace Alpha.Scada.Tests;

public sealed class NatsSecurityTests
{
    [Fact]
    public async Task Nats_rejects_bad_credentials_and_accepts_edge_ingress()
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

            var nats = await ContainerSupport.StartOrSkipAsync(async () =>
            {
                var container = new ContainerBuilder()
                    .WithImage(TestImages.Nats)
                    .WithBindMount(tempDir, "/etc/nats", AccessMode.ReadOnly)
                    .WithPortBinding(4222, true)
                    .WithPortBinding(1883, true)
                    .WithCommand("-c", "/etc/nats/nats-server.conf")
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
                    .Build();
                try
                {
                    await container.StartAsync();
                    return container;
                }
                catch
                {
                    await container.DisposeAsync();
                    throw;
                }
            }, "NATS security integration test");

            await using (nats)
            {
                var natsUrl = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
                Assert.False(await CanConnectAsync(natsUrl, "wrong", "wrong"));

                var received = WaitForSubjectAsync(
                    natsUrl,
                    "alpha.demo.site.unit.telemetry");

                await Task.Delay(250);
                await PublishAsync(natsUrl, "edge", "edge-pass", "alpha.demo.site.unit.telemetry");

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

        using var cts = new CancellationTokenSource();
        var receive = Task.Run(async () =>
        {
            await foreach (var message in connection.SubscribeAsync<byte[]>(subject, cancellationToken: cts.Token)
                               .WithCancellation(cts.Token))
            {
                return message.Subject;
            }

            throw new TimeoutException($"No NATS message arrived on {subject}.");
        }, CancellationToken.None);

        try
        {
            return await receive.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            await cts.CancelAsync();
            throw new TimeoutException($"No NATS message arrived on {subject}.");
        }
    }

    private static async Task<bool> CanConnectAsync(string natsUrl, string user, string password)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = false,
            AuthOpts = new NatsAuthOpts
            {
                Username = user,
                Password = password
            }
        });
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await connection.PingAsync(cancellationToken: cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task PublishAsync(string natsUrl, string user, string password, string subject)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true,
            AuthOpts = new NatsAuthOpts
            {
                Username = user,
                Password = password
            }
        });
        await connection.PublishAsync(subject, Array.Empty<byte>());
        await connection.PingAsync();
    }
}
