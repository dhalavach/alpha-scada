/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/AlarmBroadcastTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine.Nats;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class AlarmBroadcastTests
{
    [Fact]
    public async Task Alarm_raised_is_published_to_unit_alarm_subject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-alarm-broadcast-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var postgres = PostgresContainer();
            IContainer nats;
            try
            {
                await postgres.StartAsync();
                nats = await NatsTestSupport.StartAsync(tempDir);
            }
            catch (DockerUnavailableException ex)
            {
                await postgres.DisposeAsync();
                throw SkipException.ForSkip($"Docker is not available for alarm broadcast integration test: {ex.Message}");
            }

            await using (postgres)
            await using (nats)
            {
                var connectionString = ConnectionString(postgres);
                await WaitForPostgresAsync(connectionString);
                var natsUrl = NatsTestSupport.Url(nats);
                var subject = Topics.AlarmRaisedEvent;
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, subject, TimeSpan.FromSeconds(10));

                using var host = BuildHost(connectionString, natsUrl);
                await host.StartAsync();
                await Task.Delay(250);
                await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(
                    new AlarmRaised(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        null,
                        "demo-operator",
                        "demo-site",
                        "chp-001",
                        "critical",
                        "Unit communication lost",
                        DateTimeOffset.UtcNow));

                Assert.Equal(subject, await received);
                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static IContainer PostgresContainer() =>
        new ContainerBuilder()
            .WithImage(TestImages.Postgres)
            .WithEnvironment("POSTGRES_DB", "alpha_test")
            .WithEnvironment("POSTGRES_USER", "alpha")
            .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();

    private static IHost BuildHost(string connectionString, string natsUrl)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Nats:Url"] = natsUrl
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .UseAlphaMessaging("alarm-broadcast-test", options =>
            {
                options.PublishMessage<AlarmRaised>().ToNatsSubject(Topics.AlarmRaisedEvent).UseJetStream(Topics.DomainStream);
            })
            .Build();
    }

    private static string ConnectionString(IContainer postgres) =>
        $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";

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
