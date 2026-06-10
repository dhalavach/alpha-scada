using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Alpha.Scada.ServiceDefaults.Messaging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Alpha.Scada.Tests;

public sealed class NatsSecurityTests
{
    [Fact]
    public async Task Nats_enforces_edge_ingress_permissions()
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
                    {
                      user: "edge"
                      password: "edge-pass"
                      permissions: {
                        publish: ["alpha.*.*.*.telemetry", "alpha.*.*.*.status"]
                        subscribe: ["_INBOX.>"]
                      }
                    },
                    {
                      user: "services"
                      password: "services-pass"
                      permissions: {
                        publish: [">"]
                        subscribe: [">"]
                      }
                    }
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

                await CreateEdgeStreamAsync(natsUrl);

                var telemetrySubject = Topics.Telemetry("demo", "site", "unit");
                var received = WaitForSubjectAsync(
                    natsUrl,
                    telemetrySubject,
                    user: "services",
                    password: "services-pass",
                    timeout: TimeSpan.FromSeconds(5));

                await Task.Delay(250);
                await PublishAsync(natsUrl, "edge", "edge-pass", telemetrySubject);

                Assert.Equal(telemetrySubject, await received);

                var forgedAlarm = WaitForSubjectOrNullAsync(
                    natsUrl,
                    Topics.AlarmRaisedEvent,
                    user: "services",
                    password: "services-pass",
                    timeout: TimeSpan.FromSeconds(2));
                await Task.Delay(250);
                await PublishIgnoringDeniedAsync(natsUrl, "edge", "edge-pass", Topics.AlarmRaisedEvent);
                Assert.Null(await forgedAlarm);

                var snoop = WaitForSubjectOrNullAsync(
                    natsUrl,
                    telemetrySubject,
                    user: "edge",
                    password: "edge-pass",
                    timeout: TimeSpan.FromSeconds(2));
                await Task.Delay(250);
                await PublishAsync(natsUrl, "services", "services-pass", telemetrySubject);
                Assert.Null(await snoop);

                await PublishJetStreamAsync(
                    natsUrl,
                    "edge",
                    "edge-pass",
                    telemetrySubject,
                    messageId: Guid.NewGuid().ToString("D"));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<string> WaitForSubjectAsync(
        string natsUrl,
        string subject,
        string user,
        string password,
        TimeSpan timeout)
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
            return await receive.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            await cts.CancelAsync();
            throw new TimeoutException($"No NATS message arrived on {subject}.");
        }
    }

    private static async Task<string?> WaitForSubjectOrNullAsync(
        string natsUrl,
        string subject,
        string user,
        string password,
        TimeSpan timeout)
    {
        try
        {
            return await WaitForSubjectAsync(natsUrl, subject, user, password, timeout);
        }
        catch
        {
            return null;
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

    private static async Task PublishIgnoringDeniedAsync(string natsUrl, string user, string password, string subject)
    {
        try
        {
            await PublishAsync(natsUrl, user, password, subject);
        }
        catch
        {
            // Permission failures may surface as async protocol errors depending on timing.
        }
    }

    private static async Task CreateEdgeStreamAsync(string natsUrl)
    {
        await using var connection = CreateConnection(natsUrl, "services", "services-pass");
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        await jetStream.CreateOrUpdateStreamAsync(new StreamConfig
        {
            Name = Topics.EdgeStream,
            Subjects =
            [
                Topics.TelemetryWildcard,
                Topics.StatusWildcard,
                Topics.SparkplugWildcard
            ]
        });
    }

    private static async Task PublishJetStreamAsync(
        string natsUrl,
        string user,
        string password,
        string subject,
        string messageId)
    {
        await using var connection = CreateConnection(natsUrl, user, password);
        var headers = new NatsHeaders
        {
            [RawTelemetryHeaders.NatsMessageId] = messageId
        };
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var ack = await jetStream.PublishAsync(subject, Array.Empty<byte>(), headers: headers);
        ack.EnsureSuccess();
    }

    private static NatsConnection CreateConnection(string natsUrl, string user, string password) =>
        new(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true,
            AuthOpts = new NatsAuthOpts
            {
                Username = user,
                Password = password
            }
        });
}
