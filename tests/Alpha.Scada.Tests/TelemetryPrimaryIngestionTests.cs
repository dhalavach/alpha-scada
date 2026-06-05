/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/TelemetryPrimaryIngestionTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Sockets;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Application.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Configurations;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Builder;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Hosting;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Hosting;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Logging.Abstractions;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryPrimaryIngestionTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Native_nats_telemetry_is_normalized_into_primary_storage_and_published_as_domain_event()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-telemetry-primary-test-{Guid.NewGuid():N}");
// LEARN: executes one C# statement; semicolons terminate most statements.
        Directory.CreateDirectory(tempDir);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
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
                throw SkipException.ForSkip($"Docker is not available for telemetry primary integration test: {ex.Message}");
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (nats)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (var catalog = await FakeCatalogServer.StartAsync())
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var connectionString =
// LEARN: executes one C# statement; semicolons terminate most statements.
                    $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await WaitForPostgresAsync(connectionString);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var natsUrl = NatsTestSupport.Url(nats);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var storedSubject = Topics.TelemetryStoredEvent;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, storedSubject, TimeSpan.FromSeconds(10));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var subject = Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var edgeReceived = NatsTestSupport.WaitForSubjectAsync(natsUrl, subject, TimeSpan.FromSeconds(10));

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
                using var host = BuildTelemetryHost(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    connectionString,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    natsUrl,
// LEARN: executes one C# statement; semicolons terminate most statements.
                    catalog.BaseAddress);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.Services.GetRequiredService<TelemetryMigrator>().MigrateAsync(CancellationToken.None);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.StartAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(250);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var timestamp = DateTimeOffset.UtcNow;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var payload = JsonSerializer.SerializeToUtf8Bytes(
// LEARN: creates a new object or record instance.
                    new TelemetryEnvelopeV1(
// LEARN: continues an argument/object/collection initializer onto the next line.
                        TelemetryEnvelopeV1.SchemaVersion,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "chp-demo-001",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        timestamp,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        [new("engine.electrical_output_kw", 61.2, "good", timestamp)]),
// LEARN: serializes or deserializes JSON using System.Text.Json.
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var messageId = Guid.NewGuid().ToString("D");
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await NatsTestSupport.PublishAsync(natsUrl, subject, payload, messageId);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await NatsTestSupport.PublishAsync(natsUrl, subject, payload, messageId);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(subject, await edgeReceived);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await WaitForRowsAsync(connectionString, "telemetry_samples", 1, TimeSpan.FromSeconds(10));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(storedSubject, await received);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var deadLetterSubject = Topics.Dlq("telemetry", subject);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var malformedDeadLetter = NatsTestSupport.WaitForSubjectAsync(natsUrl, deadLetterSubject, TimeSpan.FromSeconds(10));
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await NatsTestSupport.PublishAsync(natsUrl, subject, Encoding.UTF8.GetBytes("{"), Guid.NewGuid().ToString("D"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(deadLetterSubject, await malformedDeadLetter);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var unsupportedSchemaPayload = JsonSerializer.SerializeToUtf8Bytes(
// LEARN: creates a new object or record instance.
                    new TelemetryEnvelopeV1(
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "2.0",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "chp-demo-001",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        DateTimeOffset.UtcNow,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        []),
// LEARN: serializes or deserializes JSON using System.Text.Json.
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var unsupportedSchemaDeadLetter = NatsTestSupport.WaitForSubjectAsync(natsUrl, deadLetterSubject, TimeSpan.FromSeconds(10));
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await NatsTestSupport.PublishAsync(natsUrl, subject, unsupportedSchemaPayload, Guid.NewGuid().ToString("D"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(deadLetterSubject, await unsupportedSchemaDeadLetter);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.Services.GetRequiredService<TelemetryRepository>().IngestAsync(
// LEARN: creates a new object or record instance.
                    new TelemetryIngestRequest(
// LEARN: continues an argument/object/collection initializer onto the next line.
                        TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        UnitId,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                        [
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                            new(
// LEARN: continues an argument/object/collection initializer onto the next line.
                                TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                                "engine.electrical_output_kw",
// LEARN: continues an argument/object/collection initializer onto the next line.
                                "Electrical Output",
// LEARN: continues an argument/object/collection initializer onto the next line.
                                "Engine",
// LEARN: continues an argument/object/collection initializer onto the next line.
                                "kW",
// LEARN: continues an argument/object/collection initializer onto the next line.
                                45,
// LEARN: continues an argument/object/collection initializer onto the next line.
                                70,
// LEARN: continues an argument/object/collection initializer onto the next line.
                                61.2,
// LEARN: continues an argument/object/collection initializer onto the next line.
                                "good",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                                timestamp)
// LEARN: continues an argument/object/collection initializer onto the next line.
                        ]),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
                    CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));

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

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Telemetry_repository_transaction_overload_rolls_back_and_commits_samples_with_current_state()
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
            throw SkipException.ForSkip($"Docker is not available for telemetry transaction integration test: {ex.Message}");
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
            await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new TelemetryRepository(dataSource);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var request = IngestRequest(DateTimeOffset.UtcNow);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (var connection = await dataSource.OpenConnectionAsync())
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (var transaction = await connection.BeginTransactionAsync())
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await repository.IngestAsync(connection, transaction, request, CancellationToken.None);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await transaction.RollbackAsync();
            }

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(0, await CountRowsAsync(connectionString, "telemetry_samples"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(0, await CountRowsAsync(connectionString, "tag_current"));

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (var connection = await dataSource.OpenConnectionAsync())
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (var transaction = await connection.BeginTransactionAsync())
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await repository.IngestAsync(connection, transaction, request, CancellationToken.None);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await transaction.CommitAsync();
            }

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static IHost BuildTelemetryHost(string connectionString, string natsUrl, string catalogBaseAddress)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var settings = new Dictionary<string, string?>
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["ConnectionStrings:Postgres"] = connectionString,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["Nats:Url"] = natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["Services:Tenant"] = catalogBaseAddress,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["Services:Asset"] = catalogBaseAddress,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ["Services:TagCatalog"] = catalogBaseAddress
        };

// LEARN: returns a value or exits the current method.
        return Host.CreateDefaultBuilder()
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .ConfigureServices((context, services) =>
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddServiceDatabase(context.Configuration);
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<TelemetryMigrator>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<TelemetryRepository>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<CatalogCache>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<ITelemetryAdapter, NatsJsonTelemetryAdapter>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<TelemetryAdapterResolver>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<CanonicalTelemetryHandler>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddHostedService<TelemetryAdapterIngestionWorker>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddMemoryCache();
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                services.AddAlphaServiceClients(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    context.Configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    AlphaServiceClients.Tenant,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    AlphaServiceClients.Asset,
// LEARN: executes one C# statement; semicolons terminate most statements.
                    AlphaServiceClients.TagCatalog);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
            .UseAlphaMessaging("telemetry-primary-test", options =>
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                options.Discovery.IncludeAssembly(typeof(CanonicalTelemetryHandler).Assembly);
// LEARN: executes one C# statement; semicolons terminate most statements.
                Alpha.Scada.Telemetry.MessagingTopology.Configure(options);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static TelemetryIngestRequest IngestRequest(DateTimeOffset timestamp) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new(
// LEARN: continues an argument/object/collection initializer onto the next line.
            TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            UnitId,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            [
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                new(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    "engine.electrical_output_kw",
// LEARN: continues an argument/object/collection initializer onto the next line.
                    "Electrical Output",
// LEARN: continues an argument/object/collection initializer onto the next line.
                    "Engine",
// LEARN: continues an argument/object/collection initializer onto the next line.
                    "kW",
// LEARN: continues an argument/object/collection initializer onto the next line.
                    45,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    70,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    61.2,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    "good",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    timestamp)
// LEARN: executes one C# statement; semicolons terminate most statements.
            ]);

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

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
// LEARN: returns a value or exits the current method.
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task WaitForRowsAsync(string connectionString, string tableName, int expected, TimeSpan timeout)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
// LEARN: starts a loop that continues while its condition remains true.
        while (DateTimeOffset.UtcNow < deadline)
        {
// LEARN: branches only when the boolean condition is true.
            if (await CountRowsAsync(connectionString, tableName) >= expected)
            {
// LEARN: returns a value or exits the current method.
                return;
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await Task.Delay(250);
        }

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(expected, await CountRowsAsync(connectionString, tableName));
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class FakeCatalogServer(WebApplication app) : IAsyncDisposable
    {
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public string BaseAddress { get; } = app.Urls.Single();

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        public static async Task<FakeCatalogServer> StartAsync()
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var port = GetFreePort();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var builder = WebApplication.CreateSlimBuilder();
// LEARN: executes one C# statement; semicolons terminate most statements.
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var app = builder.Build();

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
            app.MapGet("/internal/v1/tenants/resolve/{tenantKey}", (string tenantKey) =>
// LEARN: creates an ASP.NET Core HTTP response result.
                Results.Ok(new TenantDto(TenantId, tenantKey, "Demo Operator", "EU")));

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
            app.MapGet("/internal/v1/units/resolve", (Guid tenantId, string siteKey, string unitKey) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                tenantId == TenantId && siteKey == "demo-energy-site" && unitKey == "chp-demo-001"
// LEARN: creates a new object or record instance.
                    ? Results.Ok(new ResolvedUnitDto(TenantId, SiteId, UnitId, "Combined Heat and Power Unit 001", "online"))
// LEARN: executes one C# statement; semicolons terminate most statements.
                    : Results.NotFound());

// LEARN: registers an HTTP POST endpoint in ASP.NET Core Minimal APIs.
            app.MapPost("/internal/v1/tags/resolve", (ResolveTagsRequest request) =>
// LEARN: creates an ASP.NET Core HTTP response result.
                Results.Ok<IReadOnlyCollection<TagDto>>([
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    new(
// LEARN: continues an argument/object/collection initializer onto the next line.
                        TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        request.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        request.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "engine.electrical_output_kw",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "Electrical Output",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "Engine",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "kW",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        45,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                        70)
// LEARN: executes one C# statement; semicolons terminate most statements.
                ]));

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.StartAsync();
// LEARN: returns a value or exits the current method.
            return new FakeCatalogServer(app);
        }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        public async ValueTask DisposeAsync()
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.StopAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.DisposeAsync();
        }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        private static int GetFreePort()
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var listener = new TcpListener(IPAddress.Loopback, 0);
// LEARN: executes one C# statement; semicolons terminate most statements.
            listener.Start();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
// LEARN: executes one C# statement; semicolons terminate most statements.
            listener.Stop();
// LEARN: returns a value or exits the current method.
            return port;
        }
    }
}
