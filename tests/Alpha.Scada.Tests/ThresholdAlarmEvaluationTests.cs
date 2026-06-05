/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ThresholdAlarmEvaluationTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ThresholdAlarmEvaluationTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid HighTagId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid NormalTagId = Guid.Parse("40000000-0000-0000-0000-000000000002");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Threshold_evaluation_raises_once_dedupes_repeats_then_clears()
    {
        var postgres = new ContainerBuilder()
            .WithImage(TestImages.Postgres)
            .WithEnvironment("POSTGRES_DB", "alpha_test")
            .WithEnvironment("POSTGRES_USER", "alpha")
            .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();

// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.StartAsync();
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (DockerUnavailableException ex)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.DisposeAsync();
            throw SkipException.ForSkip($"Docker is not available for threshold alarm integration test: {ex.Message}");
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
            var connectionString =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AlarmRepository(dataSource);

            // Batch with one breaching tag (raise) and one healthy tag (no-op) in a single set-based call.
            var firstBreach = await repository.EvaluateAsync(
                Request(Breaching(HighTagId), Healthy(NormalTagId)),
                CancellationToken.None);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(firstBreach.Raised);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(firstBreach.Cleared);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(HighTagId, firstBreach.Raised.Single().TagId);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountActiveAsync(connectionString));

            // Repeat breach for the same tag must not raise a second active alarm (atomic de-dup).
            var repeatBreach = await repository.EvaluateAsync(
                Request(Breaching(HighTagId)),
                CancellationToken.None);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(repeatBreach.Raised);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountActiveAsync(connectionString));

            // Tag returns to range: the open alarm is cleared.
            var recovery = await repository.EvaluateAsync(
                Request(Healthy(HighTagId)),
                CancellationToken.None);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(recovery.Cleared);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(HighTagId, recovery.Cleared.Single().TagId);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal("cleared", recovery.Cleared.Single().State);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(0, await CountActiveAsync(connectionString));
        }
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static AlarmEvaluationRequest Request(params ResolvedTelemetrySample[] samples) =>
        new(TenantId, UnitId, "Combined Heat and Power Unit 001", samples);

    // value 150 is above the high threshold of 100 -> alarm.
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Breaching(Guid tagId) =>
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 150, "good", DateTimeOffset.UtcNow);

    // value 50 sits inside [0, 100] with good quality -> no alarm.
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Healthy(Guid tagId) =>
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 50, "good", DateTimeOffset.UtcNow);

    private static async Task<int> CountActiveAsync(string connectionString)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(
            "select count(*) from alarm_events where state in ('active', 'acknowledged')",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
// LEARN: starts a loop that continues while its condition remains true.
        while (true)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await connection.OpenAsync();
                return;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch when (DateTimeOffset.UtcNow < deadline)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(500);
            }
        }
    }
}
