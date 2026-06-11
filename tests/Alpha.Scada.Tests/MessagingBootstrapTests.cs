using Alpha.Scada.ServiceDefaults.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class MessagingBootstrapTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task UseAlphaMessaging_starts_and_stops_with_postgres_and_nats()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-messaging-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "messaging bootstrap NATS integration test");
            await using (nats)
            {
                var connectionString = await postgres.CreateDatabaseAsync(nameof(MessagingBootstrapTests));
                await WaitForPostgresAsync(connectionString);

                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = connectionString,
                    ["Nats:Url"] = NatsTestSupport.Url(nats)
                };

                using var host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
                    .UseAlphaMessaging("test")
                    .Build();

                await host.StartAsync();
                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Gateway_only_creates_a_durable_consumer_for_report_completion()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-gateway-messaging-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "gateway messaging topology NATS integration test");
            await using (nats)
            {
                var connectionString = await postgres.CreateDatabaseAsync(
                    $"{nameof(MessagingBootstrapTests)}_Gateway");
                await WaitForPostgresAsync(connectionString);
                var natsUrl = NatsTestSupport.Url(nats);
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = connectionString,
                    ["Nats:Url"] = natsUrl
                };

                using var host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
                    .UseAlphaMessaging(
                        "gateway-topology-test",
                        Alpha.Scada.Gateway.MessagingTopology.Configure)
                    .Build();

                await host.StartAsync();
                var consumers = await WaitForReportConsumersAsync(natsUrl);
                var domainConsumers = await ConsumerNamesAsync(natsUrl, Topics.DomainStream);
                await host.StopAsync();

                var reportConsumer = Assert.Single(consumers);
                Assert.Equal("gateway-report-completed", reportConsumer.Name);
                Assert.Null(reportConsumer.FilterSubject);
                Assert.Empty(domainConsumers);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<DomainConsumer>> WaitForReportConsumersAsync(string natsUrl)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            await using var connection = new NatsConnection(new NatsOpts
            {
                Url = natsUrl,
                RetryOnInitialConnect = true
            });
            var jetStream = new NatsJSContextFactory().CreateContext(connection);
            var consumers = new List<DomainConsumer>();
            await foreach (var consumer in jetStream.ListConsumersAsync(Topics.ReportsStream))
            {
                consumers.Add(new DomainConsumer(
                    consumer.Info.Name,
                    consumer.Info.Config.FilterSubject));
            }

            if (consumers.Count > 0 || DateTimeOffset.UtcNow >= deadline)
            {
                return consumers.OrderBy(consumer => consumer.Name).ToArray();
            }

            await Task.Delay(100);
        }
    }

    private static async Task<IReadOnlyList<string>> ConsumerNamesAsync(string natsUrl, string streamName)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true
        });
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var names = new List<string>();
        await foreach (var name in jetStream.ListConsumerNamesAsync(streamName))
        {
            names.Add(name);
        }

        return names.Order().ToArray();
    }

    private sealed record DomainConsumer(string Name, string? FilterSubject);

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
