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

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmBroadcastTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Alarm_raised_is_published_to_unit_alarm_subject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-alarm-broadcast-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            var postgres = PostgresContainer();
            IContainer nats;
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await postgres.StartAsync();
                nats = await NatsTestSupport.StartAsync(tempDir);
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (DockerUnavailableException ex)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await postgres.DisposeAsync();
                throw SkipException.ForSkip($"Docker is not available for alarm broadcast integration test: {ex.Message}");
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (nats)
            {
                var connectionString = ConnectionString(postgres);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await WaitForPostgresAsync(connectionString);
                var natsUrl = NatsTestSupport.Url(nats);
                var subject = Topics.AlarmRaisedEvent;
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, subject, TimeSpan.FromSeconds(10));

                using var host = BuildHost(connectionString, natsUrl);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.StartAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(250);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
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

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(subject, await received);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.StopAsync();
            }
        }
// LEARN: runs cleanup code whether or not the try block failed.
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
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
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
            .UseAlphaMessaging("alarm-broadcast-test", options =>
            {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<AlarmRaised>().ToNatsSubject(Topics.AlarmRaisedEvent).UseJetStream(Topics.DomainStream);
            })
            .Build();
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static string ConnectionString(IContainer postgres) =>
        $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";

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
