using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine.Nats;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class StatusBroadcastTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task Unit_status_changed_is_published_to_unit_status_subject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-status-broadcast-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "status broadcast NATS integration test");
            await using (nats)
            {
                var connectionString = await postgres.CreateDatabaseAsync(nameof(StatusBroadcastTests));
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
