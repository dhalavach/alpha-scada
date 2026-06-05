/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/TelemetryPrimaryIngestionTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.Contracts.Messaging;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Application;
using Alpha.Scada.Telemetry.Application.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class TelemetryPrimaryIngestionTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Native_nats_telemetry_is_normalized_into_primary_storage_and_published_as_domain_event()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-telemetry-primary-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
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
            try
            {
                await postgres.StartAsync();
                nats = await NatsTestSupport.StartAsync(tempDir);
            }
            catch (DockerUnavailableException ex)
            {
                await postgres.DisposeAsync();
                throw SkipException.ForSkip($"Docker is not available for telemetry primary integration test: {ex.Message}");
            }

            await using (postgres)
            await using (nats)
            await using (var catalog = await FakeCatalogServer.StartAsync())
            {
                var connectionString =
                    $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
                await WaitForPostgresAsync(connectionString);

                var natsUrl = NatsTestSupport.Url(nats);
                var storedSubject = Topics.TelemetryStoredEvent;
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, storedSubject, TimeSpan.FromSeconds(10));
                var subject = Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001");
                var edgeReceived = NatsTestSupport.WaitForSubjectAsync(natsUrl, subject, TimeSpan.FromSeconds(10));

                using var host = BuildTelemetryHost(
                    connectionString,
                    natsUrl,
                    catalog.BaseAddress);

                await host.Services.GetRequiredService<TelemetryMigrator>().MigrateAsync(CancellationToken.None);
                await host.StartAsync();
                await Task.Delay(250);
                var timestamp = DateTimeOffset.UtcNow;
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    new TelemetryEnvelopeV1(
                        TelemetryEnvelopeV1.SchemaVersion,
                        "chp-demo-001",
                        timestamp,
                        [new("engine.electrical_output_kw", 61.2, "good", timestamp)]),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var messageId = Guid.NewGuid().ToString("D");
                await NatsTestSupport.PublishAsync(natsUrl, subject, payload, messageId);
                await NatsTestSupport.PublishAsync(natsUrl, subject, payload, messageId);

                Assert.Equal(subject, await edgeReceived);
                await WaitForRowsAsync(connectionString, "telemetry_samples", 1, TimeSpan.FromSeconds(10));
                Assert.Equal(storedSubject, await received);

                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
                Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));

                var deadLetterSubject = Topics.Dlq("telemetry", subject);
                var malformedDeadLetter = NatsTestSupport.WaitForSubjectAsync(natsUrl, deadLetterSubject, TimeSpan.FromSeconds(10));
                await NatsTestSupport.PublishAsync(natsUrl, subject, Encoding.UTF8.GetBytes("{"), Guid.NewGuid().ToString("D"));
                Assert.Equal(deadLetterSubject, await malformedDeadLetter);
                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));

                var unsupportedSchemaPayload = JsonSerializer.SerializeToUtf8Bytes(
                    new TelemetryEnvelopeV1(
                        "2.0",
                        "chp-demo-001",
                        DateTimeOffset.UtcNow,
                        []),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var unsupportedSchemaDeadLetter = NatsTestSupport.WaitForSubjectAsync(natsUrl, deadLetterSubject, TimeSpan.FromSeconds(10));
                await NatsTestSupport.PublishAsync(natsUrl, subject, unsupportedSchemaPayload, Guid.NewGuid().ToString("D"));
                Assert.Equal(deadLetterSubject, await unsupportedSchemaDeadLetter);
                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));

                await host.Services.GetRequiredService<TelemetryRepository>().IngestAsync(
                    new TelemetryIngestRequest(
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
                                61.2,
                                "good",
                                timestamp)
                        ]),
                    CancellationToken.None);

                Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
                Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));

                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Telemetry_repository_transaction_overload_rolls_back_and_commits_samples_with_current_state()
    {
        var postgres = new ContainerBuilder()
            .WithImage(TestImages.Postgres)
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
            throw SkipException.ForSkip($"Docker is not available for telemetry transaction integration test: {ex.Message}");
        }

        await using (postgres)
        {
            var connectionString =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
            await WaitForPostgresAsync(connectionString);

            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TelemetryRepository(dataSource);
            var request = IngestRequest(DateTimeOffset.UtcNow);

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await repository.IngestAsync(connection, transaction, request, CancellationToken.None);
                await transaction.RollbackAsync();
            }

            Assert.Equal(0, await CountRowsAsync(connectionString, "telemetry_samples"));
            Assert.Equal(0, await CountRowsAsync(connectionString, "tag_current"));

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await repository.IngestAsync(connection, transaction, request, CancellationToken.None);
                await transaction.CommitAsync();
            }

            Assert.Equal(1, await CountRowsAsync(connectionString, "telemetry_samples"));
            Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));
        }
    }

    private static IHost BuildTelemetryHost(string connectionString, string natsUrl, string catalogBaseAddress)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Nats:Url"] = natsUrl,
            ["Services:Tenant"] = catalogBaseAddress,
            ["Services:Asset"] = catalogBaseAddress,
            ["Services:TagCatalog"] = catalogBaseAddress
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services.AddServiceDatabase(context.Configuration);
                services.AddSingleton<TelemetryMigrator>();
                services.AddSingleton<TelemetryRepository>();
                services.AddSingleton<CatalogCache>();
                services.AddSingleton<ITelemetryAdapter, NatsJsonTelemetryAdapter>();
                services.AddSingleton<TelemetryAdapterResolver>();
                services.AddSingleton<CanonicalTelemetryHandler>();
                services.AddHostedService<TelemetryAdapterIngestionWorker>();
                services.AddMemoryCache();
                services.AddAlphaServiceClients(
                    context.Configuration,
                    AlphaServiceClients.Tenant,
                    AlphaServiceClients.Asset,
                    AlphaServiceClients.TagCatalog);
            })
            .UseAlphaMessaging("telemetry-primary-test", options =>
            {
                options.Discovery.IncludeAssembly(typeof(CanonicalTelemetryHandler).Assembly);
                Alpha.Scada.Telemetry.MessagingTopology.Configure(options);
            })
            .Build();
    }

    private static TelemetryIngestRequest IngestRequest(DateTimeOffset timestamp) =>
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
                    61.2,
                    "good",
                    timestamp)
            ]);

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

    private static async Task WaitForRowsAsync(string connectionString, string tableName, int expected, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await CountRowsAsync(connectionString, tableName) >= expected)
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Equal(expected, await CountRowsAsync(connectionString, tableName));
    }

    private sealed class FakeCatalogServer(WebApplication app) : IAsyncDisposable
    {
        public string BaseAddress { get; } = app.Urls.Single();

        public static async Task<FakeCatalogServer> StartAsync()
        {
            var port = GetFreePort();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build();

            app.MapGet("/internal/v1/tenants/resolve/{tenantKey}", (string tenantKey) =>
                Results.Ok(new TenantDto(TenantId, tenantKey, "Demo Operator", "EU")));

            app.MapGet("/internal/v1/units/resolve", (Guid tenantId, string siteKey, string unitKey) =>
                tenantId == TenantId && siteKey == "demo-energy-site" && unitKey == "chp-demo-001"
                    ? Results.Ok(new ResolvedUnitDto(TenantId, SiteId, UnitId, "Combined Heat and Power Unit 001", "online"))
                    : Results.NotFound());

            app.MapPost("/internal/v1/tags/resolve", (ResolveTagsRequest request) =>
                Results.Ok<IReadOnlyCollection<TagDto>>([
                    new(
                        TagId,
                        request.TenantId,
                        request.UnitId,
                        "engine.electrical_output_kw",
                        "Electrical Output",
                        "Engine",
                        "kW",
                        45,
                        70)
                ]));

            await app.StartAsync();
            return new FakeCatalogServer(app);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
