using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class DurableOutboxTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Telemetry_ingest_commits_samples_current_state_and_outbox_event_together()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TelemetryRepository(dataSource, new DomainOutbox());
            var timestamp = DateTimeOffset.UtcNow;
            var olderTimestamp = timestamp.Subtract(TimeSpan.FromMinutes(5));

            await repository.IngestAsync(
                TelemetryRequest(61.2, timestamp),
                CancellationToken.None,
                [new TelemetryBatchStored(
                    TenantId,
                    UnitId,
                    "demo-operator",
                    "demo-site",
                    "chp-001",
                    timestamp,
                    [new StoredSample(TagId, "engine.electrical_output_kw", 61.2, "good", timestamp)])]);

            await repository.IngestAsync(TelemetryRequest(12.3, olderTimestamp), CancellationToken.None);

            Assert.Equal(2, await CountRowsAsync(connectionString, "telemetry_samples"));
            Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));
            Assert.Equal(1, await CountPendingOutboxAsync(connectionString));
            Assert.Equal(61.2, await CurrentValueAsync(connectionString));
        });
    }

    [Fact]
    public async Task Alarm_evaluation_commits_alarm_state_and_outbox_events_together()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AlarmRepository(dataSource, new DomainOutbox());
            var route = new AlarmRouteKeys(TenantId, UnitId, "demo-operator", "demo-site", "chp-001");

            var raised = await repository.EvaluateAsync(
                AlarmRequest(150),
                route,
                CancellationToken.None);

            Assert.Single(raised.Raised);
            Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));
            Assert.Equal(1, await CountPendingOutboxAsync(connectionString));

            var cleared = await repository.EvaluateAsync(
                AlarmRequest(50),
                route,
                CancellationToken.None);

            Assert.Single(cleared.Cleared);
            Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));
            Assert.Equal(2, await CountPendingOutboxAsync(connectionString));
        });
    }

    [Fact]
    public async Task Asset_status_update_commits_unit_state_and_outbox_event_together()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AssetRepository(dataSource, new DomainOutbox());

            var unit = await repository.SetUnitOnlineAsync(
                UnitId,
                new UnitStatusRoute("demo-operator", "demo-energy-site", "chp-demo-001"),
                CancellationToken.None);

            Assert.NotNull(unit);
            Assert.Equal("online", unit.Status);
            Assert.Equal(1, await CountPendingOutboxAsync(connectionString));
        });
    }

    private static TelemetryIngestRequest TelemetryRequest(double value, DateTimeOffset timestamp) =>
        new(
            TenantId,
            UnitId,
            [
                new(
                    TagId,
                    "engine.electrical_output_kw",
                    "Electrical Output",
                    "Engine",
                    "kW",
                    45,
                    70,
                    value,
                    "good",
                    timestamp)
            ]);

    private static AlarmEvaluationRequest AlarmRequest(double value) =>
        new(
            TenantId,
            UnitId,
            "Combined Heat and Power Unit 001",
            [
                new(
                    TagId,
                    "engine.electrical_output_kw",
                    "Electrical Output",
                    "Engine",
                    "kW",
                    0,
                    100,
                    value,
                    "good",
                    DateTimeOffset.UtcNow)
            ]);

    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
        var postgres = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_DB", "alpha_test")
            .WithEnvironment("POSTGRES_USER", "alpha")
            .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();

        try
        {
            await postgres.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            await postgres.DisposeAsync();
            throw SkipException.ForSkip($"Docker is not available for durable outbox integration test: {ex.Message}");
        }

        await using (postgres)
        {
            var connectionString =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
            await WaitForPostgresAsync(connectionString);
            await run(connectionString);
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

    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountPendingOutboxAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from domain_outbox_messages where dispatched_at_utc is null",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<double> CurrentValueAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select value_double from tag_current where tag_id = @tag_id",
            connection);
        command.Parameters.AddWithValue("tag_id", TagId);
        return Convert.ToDouble(await command.ExecuteScalarAsync());
    }
}
