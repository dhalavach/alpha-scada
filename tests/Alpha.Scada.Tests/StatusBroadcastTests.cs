/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/StatusBroadcastTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Configurations;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Hosting;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine.Nats;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class StatusBroadcastTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Unit_status_changed_is_published_to_unit_status_subject()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-status-broadcast-test-{Guid.NewGuid():N}");
// LEARN: executes one C# statement; semicolons terminate most statements.
        Directory.CreateDirectory(tempDir);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var postgres = PostgresContainer();
// LEARN: executes one C# statement; semicolons terminate most statements.
            IContainer nats;
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await postgres.StartAsync();
// LEARN: executes one C# statement; semicolons terminate most statements.
                nats = await NatsTestSupport.StartAsync(tempDir);
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (DockerUnavailableException ex)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await postgres.DisposeAsync();
// LEARN: throws an exception to signal that this path cannot continue safely.
                throw SkipException.ForSkip($"Docker is not available for status broadcast integration test: {ex.Message}");
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (nats)
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var connectionString = ConnectionString(postgres);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await WaitForPostgresAsync(connectionString);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var natsUrl = NatsTestSupport.Url(nats);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var subject = Topics.StatusChangedEvent;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, subject, TimeSpan.FromSeconds(10));

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
                using var host = BuildHost(connectionString, natsUrl);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.StartAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(250);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(
// LEARN: creates a new object or record instance.
                    new UnitStatusChanged(
// LEARN: continues an argument/object/collection initializer onto the next line.
                        Guid.NewGuid(),
// LEARN: continues an argument/object/collection initializer onto the next line.
                        Guid.NewGuid(),
// LEARN: continues an argument/object/collection initializer onto the next line.
                        Guid.NewGuid(),
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "demo-operator",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "demo-site",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "chp-001",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "CHP 001",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "online",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        DateTimeOffset.UtcNow,
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
            Directory.Delete(tempDir, recursive: true);
        }
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static IContainer PostgresContainer() =>
// LEARN: creates a new object or record instance.
        new ContainerBuilder()
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

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static IHost BuildHost(string connectionString, string natsUrl)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var settings = new Dictionary<string, string?>
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["ConnectionStrings:Postgres"] = connectionString,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ["Nats:Url"] = natsUrl
        };

// LEARN: returns a value or exits the current method.
        return Host.CreateDefaultBuilder()
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
            .UseAlphaMessaging("status-broadcast-test", options =>
            {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<UnitStatusChanged>().ToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static string ConnectionString(IContainer postgres) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";

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
