/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/MessagingBootstrapTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.ServiceDefaults.Messaging;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class MessagingBootstrapTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
    public async Task UseAlphaMessaging_starts_and_stops_with_postgres_and_nats()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-messaging-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            var postgres = new ContainerBuilder()
                .WithImage(TestImages.Postgres)
                .WithEnvironment("POSTGRES_DB", "alpha_test")
                .WithEnvironment("POSTGRES_USER", "alpha")
                .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
                .WithPortBinding(5432, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
                .Build();

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
                throw SkipException.ForSkip($"Docker is not available for messaging bootstrap integration test: {ex.Message}");
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (nats)
            {
                var connectionString =
                    $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await WaitForPostgresAsync(connectionString);

                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = connectionString,
                    ["Nats:Url"] = NatsTestSupport.Url(nats)
                };

                using var host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
                    .UseAlphaMessaging("test")
                    .Build();

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.StartAsync();
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
