using Alpha.Scada.ServiceDefaults.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
