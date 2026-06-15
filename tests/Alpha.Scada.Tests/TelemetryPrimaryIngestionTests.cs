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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class TelemetryPrimaryIngestionTests(PostgresContainerFixture postgres)
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
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "telemetry primary NATS integration test");
            await using (nats)
            await using (var catalog = await FakeCatalogServer.StartAsync())
            {
                var connectionString = await postgres.CreateDatabaseAsync(nameof(TelemetryPrimaryIngestionTests));
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
    public async Task Invalid_telemetry_payload_is_dead_lettered_durably_with_payload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-telemetry-dlq-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "telemetry DLQ NATS integration test");
            await using (nats)
            await using (var catalog = await FakeCatalogServer.StartAsync())
            {
                var connectionString = await postgres.CreateDatabaseAsync(nameof(TelemetryPrimaryIngestionTests));
                await WaitForPostgresAsync(connectionString);

                var natsUrl = NatsTestSupport.Url(nats);
                using var host = BuildTelemetryHost(
                    connectionString,
                    natsUrl,
                    catalog.BaseAddress);

                await host.Services.GetRequiredService<TelemetryMigrator>().MigrateAsync(CancellationToken.None);
                await host.StartAsync();
                await Task.Delay(250);

                await using var connection = new NatsConnection(new NatsOpts { Url = natsUrl, RetryOnInitialConnect = true });
                var jetStream = new NatsJSContextFactory().CreateContext(connection);
                _ = await jetStream.GetStreamAsync(Topics.DlqStream);

                var subject = Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001");
                var payload = Encoding.UTF8.GetBytes("{");
                var messageId = Guid.NewGuid().ToString("D");
                await NatsTestSupport.PublishAsync(natsUrl, subject, payload, messageId);

                var deadLetter = await FetchDeadLetterAsync(jetStream, TimeSpan.FromSeconds(10));

                Assert.Equal(subject, deadLetter.Subject);
                Assert.Equal(messageId, deadLetter.MessageId);
                Assert.Equal(nameof(JsonException), deadLetter.ErrorType);
                Assert.Equal(payload, Convert.FromBase64String(deadLetter.PayloadBase64));
                Assert.False(deadLetter.PayloadTruncated);

                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Unknown_tenant_is_dead_lettered_once_as_terminal_resolution_failure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-telemetry-resolution-dlq-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "telemetry resolution DLQ integration test");
            await using (nats)
            await using (var catalog = await FakeCatalogServer.StartAsync(rejectUnknownTenant: true))
            {
                var connectionString = await postgres.CreateDatabaseAsync($"{nameof(TelemetryPrimaryIngestionTests)}Resolution");
                await WaitForPostgresAsync(connectionString);
                var natsUrl = NatsTestSupport.Url(nats);
                using var host = BuildTelemetryHost(connectionString, natsUrl, catalog.BaseAddress);
                await host.Services.GetRequiredService<TelemetryMigrator>().MigrateAsync(CancellationToken.None);
                await host.StartAsync();
                await Task.Delay(250);

                var timestamp = DateTimeOffset.UtcNow;
                var subject = Topics.Telemetry("unknown-tenant", "demo-energy-site", "chp-demo-001");
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    new TelemetryEnvelopeV1(
                        TelemetryEnvelopeV1.SchemaVersion,
                        "chp-demo-001",
                        timestamp,
                        [new("engine.electrical_output_kw", 61.2, "good", timestamp)]),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var messageId = Guid.NewGuid().ToString("D");
                await NatsTestSupport.PublishAsync(natsUrl, subject, payload, messageId);

                await using var connection = new NatsConnection(new NatsOpts { Url = natsUrl, RetryOnInitialConnect = true });
                var jetStream = new NatsJSContextFactory().CreateContext(connection);
                var deadLetter = await FetchDeadLetterAsync(jetStream, TimeSpan.FromSeconds(10));
                await Task.Delay(500);

                Assert.Equal(nameof(TelemetryResolutionException), deadLetter.ErrorType);
                Assert.Equal(messageId, deadLetter.MessageId);
                Assert.Equal(1, catalog.TenantResolveCalls);
                Assert.Equal(0, await CountRowsAsync(connectionString, "telemetry_samples"));
                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Dead_letter_payload_factory_caps_payload_at_64_kb()
    {
        var payload = Enumerable.Range(0, DeadLetteredTelemetryFactory.MaxPayloadBytes + 5)
            .Select(value => (byte)(value % 256))
            .ToArray();

        var deadLetter = DeadLetteredTelemetryFactory.Create(
            "alpha.demo.site.unit.telemetry",
            "message-id",
            new InvalidTelemetryEnvelopeException("bad"),
            payload,
            DateTimeOffset.UtcNow);

        Assert.True(deadLetter.PayloadTruncated);
        Assert.Equal(DeadLetteredTelemetryFactory.MaxPayloadBytes, Convert.FromBase64String(deadLetter.PayloadBase64).Length);
    }

    [Fact]
    public async Task Telemetry_repository_transaction_overload_rolls_back_and_commits_samples_with_current_state()
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(TelemetryPrimaryIngestionTests));
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

    [Fact]
    public async Task Telemetry_repository_keeps_newest_current_value_when_samples_arrive_out_of_order()
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(TelemetryPrimaryIngestionTests));
        await WaitForPostgresAsync(connectionString);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new TelemetryMigrator(dataSource, NullLogger<TelemetryMigrator>.Instance).MigrateAsync(CancellationToken.None);
        var repository = new TelemetryRepository(dataSource);
        var older = DateTimeOffset.UtcNow.AddSeconds(-1);
        var newer = DateTimeOffset.UtcNow;

        await repository.IngestAsync(IngestRequest(newer, 70), CancellationToken.None);
        await repository.IngestAsync(IngestRequest(older, 55), CancellationToken.None);

        Assert.Equal(2, await CountRowsAsync(connectionString, "telemetry_samples"));
        Assert.Equal(1, await CountRowsAsync(connectionString, "tag_current"));
        Assert.Equal(70, await CurrentValueAsync(connectionString));
    }

    private static IHost BuildTelemetryHost(string connectionString, string natsUrl, string catalogBaseAddress)
    {
        var settings = TestJwt.Settings(
            ("ConnectionStrings:Postgres", connectionString),
            ("Nats:Url", natsUrl),
            ("Services:Tenant", catalogBaseAddress),
            ("Services:Asset", catalogBaseAddress),
            ("Services:TagCatalog", catalogBaseAddress),
            ("Telemetry:Ingestion:MaxDegreeOfParallelism", "8"));

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
                services.AddSingleton(sp => TelemetryIngestionOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
                services.AddSingleton<TelemetryIngestionMetrics>();
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

    private static TelemetryIngestRequest IngestRequest(DateTimeOffset timestamp, double value = 61.2) =>
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

    private static async Task<double> CurrentValueAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select value_double from tag_current where tag_id = @tag_id", connection);
        command.Parameters.AddWithValue("tag_id", TagId);
        return Convert.ToDouble(await command.ExecuteScalarAsync());
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

    private static async Task<DeadLetteredTelemetry> FetchDeadLetterAsync(INatsJSContext jetStream, TimeSpan timeout)
    {
        var consumer = await jetStream.CreateConsumerAsync(
            Topics.DlqStream,
            new ConsumerConfig($"dlq-test-{Guid.NewGuid():N}")
            {
                FilterSubject = Topics.DlqWildcard,
                InactiveThreshold = TimeSpan.FromMinutes(1)
            });
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var message in consumer.FetchAsync<byte[]>(
                           new NatsJSFetchOpts { MaxMsgs = 1, Expires = timeout },
                           cancellationToken: cts.Token))
        {
            message.EnsureSuccess();
            var deadLetter = JsonSerializer.Deserialize<DeadLetteredTelemetry>(
                message.Data,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await message.AckAsync(cancellationToken: cts.Token);
            return deadLetter ?? throw new InvalidOperationException("DLQ message was empty.");
        }

        throw new TimeoutException("No telemetry DLQ message arrived.");
    }

    private sealed class FakeCatalogServer(WebApplication app, FakeCatalogRequestCounter counter) : IAsyncDisposable
    {
        public string BaseAddress { get; } = app.Urls.Single();

        public int TenantResolveCalls => Volatile.Read(ref counter.TenantResolveCalls);

        public static async Task<FakeCatalogServer> StartAsync(bool rejectUnknownTenant = false)
        {
            var port = GetFreePort();
            var counter = new FakeCatalogRequestCounter();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build();

            app.MapGet("/internal/v1/tenants/resolve/{tenantKey}", (string tenantKey) =>
            {
                Interlocked.Increment(ref counter.TenantResolveCalls);
                return rejectUnknownTenant && tenantKey != "demo-operator"
                    ? Results.NotFound()
                    : Results.Ok(new TenantDto(TenantId, tenantKey, "Demo Operator", "EU"));
            });

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
            return new FakeCatalogServer(app, counter);
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

    private sealed class FakeCatalogRequestCounter
    {
        public int TenantResolveCalls;
    }
}
