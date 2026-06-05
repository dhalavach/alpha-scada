/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/NatsSecurityTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NATS.Client.Core;
using Xunit.Sdk;

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

            var nats = new ContainerBuilder()
                .WithImage(TestImages.Nats)
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
