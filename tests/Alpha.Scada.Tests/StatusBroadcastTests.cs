using Alpha.Scada.Asset.Contracts;
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

public sealed class StatusBroadcastTests
{
    [Fact]
    public async Task Unit_status_changed_is_published_to_unit_status_subject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-status-broadcast-test-{Guid.NewGuid():N}");
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
                throw SkipException.ForSkip($"Docker is not available for status broadcast integration test: {ex.Message}");
            }

            await using (postgres)
            await using (nats)
            {
                var connectionString = ConnectionString(postgres);
                await WaitForPostgresAsync(connectionString);
                var natsUrl = NatsTestSupport.Url(nats);
                var subject = Topics.StatusChangedEvent;
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, subject, TimeSpan.FromSeconds(10));

                using var host = BuildHost(connectionString, natsUrl);
                await host.StartAsync();
                await Task.Delay(250);
                await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(
                    new UnitStatusChanged(
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
            .UseAlphaMessaging("status-broadcast-test", options =>
            {
                options.PublishMessage<UnitStatusChanged>().ToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream);
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
