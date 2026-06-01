using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class ThresholdAlarmEvaluationTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid HighTagId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid NormalTagId = Guid.Parse("40000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Threshold_evaluation_raises_once_dedupes_repeats_then_clears()
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
            throw SkipException.ForSkip($"Docker is not available for threshold alarm integration test: {ex.Message}");
        }

        await using (postgres)
        {
            var connectionString =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
            await WaitForPostgresAsync(connectionString);

            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AlarmRepository(dataSource);

            // Batch with one breaching tag (raise) and one healthy tag (no-op) in a single set-based call.
            var firstBreach = await repository.EvaluateAsync(
                Request(Breaching(HighTagId), Healthy(NormalTagId)),
                CancellationToken.None);
            Assert.Single(firstBreach.Raised);
            Assert.Empty(firstBreach.Cleared);
            Assert.Equal(HighTagId, firstBreach.Raised.Single().TagId);
            Assert.Equal(1, await CountActiveAsync(connectionString));

            // Repeat breach for the same tag must not raise a second active alarm (atomic de-dup).
            var repeatBreach = await repository.EvaluateAsync(
                Request(Breaching(HighTagId)),
                CancellationToken.None);
            Assert.Empty(repeatBreach.Raised);
            Assert.Equal(1, await CountActiveAsync(connectionString));

            // Tag returns to range: the open alarm is cleared.
            var recovery = await repository.EvaluateAsync(
                Request(Healthy(HighTagId)),
                CancellationToken.None);
            Assert.Single(recovery.Cleared);
            Assert.Equal(HighTagId, recovery.Cleared.Single().TagId);
            Assert.Equal("cleared", recovery.Cleared.Single().State);
            Assert.Equal(0, await CountActiveAsync(connectionString));
        }
    }

    private static AlarmEvaluationRequest Request(params ResolvedTelemetrySample[] samples) =>
        new(TenantId, UnitId, "Combined Heat and Power Unit 001", samples);

    // value 150 is above the high threshold of 100 -> alarm.
    private static ResolvedTelemetrySample Breaching(Guid tagId) =>
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 150, "good", DateTimeOffset.UtcNow);

    // value 50 sits inside [0, 100] with good quality -> no alarm.
    private static ResolvedTelemetrySample Healthy(Guid tagId) =>
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 50, "good", DateTimeOffset.UtcNow);

    private static async Task<int> CountActiveAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from alarm_events where state in ('active', 'acknowledged')",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
