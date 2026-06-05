/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ThresholdAlarmEvaluationTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Logging.Abstractions;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ThresholdAlarmEvaluationTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid HighTagId = Guid.Parse("40000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid NormalTagId = Guid.Parse("40000000-0000-0000-0000-000000000002");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Threshold_evaluation_raises_once_dedupes_repeats_then_clears()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var postgres = new ContainerBuilder()
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithImage(TestImages.Postgres)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithEnvironment("POSTGRES_DB", "alpha_test")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithEnvironment("POSTGRES_USER", "alpha")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithPortBinding(5432, true)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw SkipException.ForSkip($"Docker is not available for threshold alarm integration test: {ex.Message}");
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var connectionString =
// LEARN: executes one C# statement; semicolons terminate most statements.
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new AlarmRepository(dataSource);

            // Batch with one breaching tag (raise) and one healthy tag (no-op) in a single set-based call.
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var firstBreach = await repository.EvaluateAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                Request(Breaching(HighTagId), Healthy(NormalTagId)),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
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
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repeatBreach = await repository.EvaluateAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                Request(Breaching(HighTagId)),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
                CancellationToken.None);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(repeatBreach.Raised);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountActiveAsync(connectionString));

            // Tag returns to range: the open alarm is cleared.
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var recovery = await repository.EvaluateAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                Request(Healthy(HighTagId)),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(TenantId, UnitId, "Combined Heat and Power Unit 001", samples);

    // value 150 is above the high threshold of 100 -> alarm.
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Breaching(Guid tagId) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 150, "good", DateTimeOffset.UtcNow);

    // value 50 sits inside [0, 100] with good quality -> no alarm.
// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Healthy(Guid tagId) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 50, "good", DateTimeOffset.UtcNow);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<int> CountActiveAsync(string connectionString)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(
// LEARN: continues an argument/object/collection initializer onto the next line.
            "select count(*) from alarm_events where state in ('active', 'acknowledged')",
// LEARN: executes one C# statement; semicolons terminate most statements.
            connection);
// LEARN: returns a value or exits the current method.
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task WaitForPostgresAsync(string connectionString)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: returns a value or exits the current method.
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
