/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/AlarmOutboxTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Net;
using System.Net.Sockets;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
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
using NATS.Client.Core;
using NATS.Client.JetStream;
using Npgsql;
using NpgsqlTypes;
using Wolverine;
using Wolverine.Nats;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmOutboxTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("50000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Alarm_evaluation_inserts_outbox_row_in_same_transaction_and_dedupes_reprocessing()
    {
        var postgres = await StartPostgresAsync();
// LEARN: branches only when the boolean condition is true.
        if (postgres is null)
        {
            return;
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
            var connectionString = ConnectionString(postgres);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var routeServer = await FakeRouteServer.StartAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var services = BuildServiceProvider(connectionString, routeServer.BaseAddress);
            var service = services.GetRequiredService<AlarmService>();

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await service.EvaluateAsync(Request(Breaching(TagId)), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_outbox"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(AlarmOutboxEvents.AlarmRaisedType, await SingleEventTypeAsync(connectionString));

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await service.EvaluateAsync(Request(Breaching(TagId)), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_outbox"));
        }
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Alarm_acknowledgement_inserts_outbox_row()
    {
        var postgres = await StartPostgresAsync();
// LEARN: branches only when the boolean condition is true.
        if (postgres is null)
        {
            return;
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
            var connectionString = ConnectionString(postgres);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var routeServer = await FakeRouteServer.StartAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var services = BuildServiceProvider(connectionString, routeServer.BaseAddress);
            var service = services.GetRequiredService<AlarmService>();
            var raised = await service.EvaluateAsync(Request(Breaching(TagId)), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await service.AcknowledgeAsync(
                raised.Raised.Single().AlarmId,
                new CurrentUserDto(UserId, TenantId, "operator@alpha.local", "Operator", "Operator"),
                CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(2, await CountRowsAsync(connectionString, "alarm_outbox"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Contains(AlarmOutboxEvents.AlarmAcknowledgedType, await EventTypesAsync(connectionString));
        }
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Dispatcher_publishes_pending_rows_marks_dispatched_and_uses_stable_dedup_id()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-alarm-outbox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            var postgres = await StartPostgresAsync();
// LEARN: branches only when the boolean condition is true.
            if (postgres is null)
            {
                return;
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
            {
                var nats = await StartNatsAsync(tempDir);
// LEARN: branches only when the boolean condition is true.
                if (nats is null)
                {
                    return;
                }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                await using (nats)
                {
                    var connectionString = ConnectionString(postgres);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await WaitForPostgresAsync(connectionString);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                    await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
                    var outboxId = await InsertOutboxAsync(connectionString, RaisedEvent());

                    using var host = BuildDispatcherHost(connectionString, NatsTestSupport.Url(nats));
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await host.StartAsync();
                    var dispatcher = host.Services.GetRequiredService<AlarmOutboxDispatcher>();

                    var firstCount = await CountSubjectMessagesAsync(
                        NatsTestSupport.Url(nats),
                        Topics.AlarmRaisedEvent,
                        TimeSpan.FromSeconds(2),
                        async () => await dispatcher.DispatchPendingAsync(CancellationToken.None));

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                    Assert.Equal(1, firstCount);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                    Assert.Equal(1, await CountDispatchedAsync(connectionString));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                    Assert.Equal(1, await CountStreamMessagesAsync(NatsTestSupport.Url(nats), Topics.DomainStream));

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await ResetOutboxRowAsync(connectionString, outboxId);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await dispatcher.DispatchPendingAsync(CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                    Assert.Equal(1, await CountStreamMessagesAsync(NatsTestSupport.Url(nats), Topics.DomainStream));
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await host.StopAsync();
                }
            }
        }
// LEARN: runs cleanup code whether or not the try block failed.
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static ServiceProvider BuildServiceProvider(string connectionString, string routeBaseAddress)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Services:Asset"] = routeBaseAddress,
            ["Services:Tenant"] = routeBaseAddress
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddServiceDatabase(configuration);
        services.AddSingleton<AlarmRepository>();
        services.AddSingleton<AlarmService>();
        services.AddSingleton<UnitKeyResolver>();
        services.AddSingleton<IAlarmOutboxSignal, NoOpOutboxSignal>();
        services.AddMemoryCache();
        services.AddAlphaServiceClients(configuration, AlphaServiceClients.Asset, AlphaServiceClients.Tenant);
        return services.BuildServiceProvider();
    }

    private static IHost BuildDispatcherHost(string connectionString, string natsUrl)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Nats:Url"] = natsUrl,
            ["AlarmOutbox:BatchSize"] = "10",
            ["AlarmOutbox:MaxAttempts"] = "3"
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services.AddServiceDatabase(context.Configuration);
                services.AddSingleton<AlarmOutboxDispatcher>();
            })
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
            .UseAlphaMessaging("alarm-outbox-dispatcher-test", options =>
            {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<AlarmRaised>().ToNatsSubject(Topics.AlarmRaisedEvent).UseJetStream(Topics.DomainStream);
            })
            .Build();
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static AlarmEvaluationRequest Request(params ResolvedTelemetrySample[] samples) =>
        new(TenantId, UnitId, "Combined Heat and Power Unit 001", samples);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Breaching(Guid tagId) =>
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 150, "good", DateTimeOffset.UtcNow);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static AlarmRaised RaisedEvent() =>
        new(Guid.NewGuid(), TenantId, UnitId, TagId, "demo-operator", "demo-site", "chp-001", "warning", "Electrical Output above high threshold", DateTimeOffset.UtcNow);

    private static async Task<IContainer?> StartPostgresAsync()
    {
        IContainer? postgres = null;
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            postgres = new ContainerBuilder()
                .WithImage(TestImages.Postgres)
                .WithEnvironment("POSTGRES_DB", "alpha_test")
                .WithEnvironment("POSTGRES_USER", "alpha")
                .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
                .WithPortBinding(5432, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
                .Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.StartAsync();
            return postgres;
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (DockerUnavailableException)
        {
// LEARN: branches only when the boolean condition is true.
            if (postgres is not null)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await postgres.DisposeAsync();
            }

            return null;
        }
    }

    private static async Task<IContainer?> StartNatsAsync(string tempDir)
    {
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            return await NatsTestSupport.StartAsync(tempDir);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (DockerUnavailableException)
        {
            return null;
        }
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static string ConnectionString(IContainer postgres) =>
        $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";

    private static async Task<Guid> InsertOutboxAsync(string connectionString, object message)
    {
        var serialized = AlarmOutboxEvents.Serialize(message);
        var id = Guid.NewGuid();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            insert into alarm_outbox (id, event_type, payload)
            values (@id, @event_type, @payload)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("event_type", serialized.EventType);
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = serialized.Payload });
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task ResetOutboxRowAsync(string connectionString, Guid outboxId)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set dispatched_at_utc = null
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", outboxId);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountSubjectMessagesAsync(
        string natsUrl,
        string subject,
        TimeSpan listenFor,
        Func<Task> action)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true
        });

        var count = 0;
        using var cts = new CancellationTokenSource();
        var subscription = Task.Run(async () =>
        {
// LEARN: asynchronously loops over a stream of values without blocking a thread.
            await foreach (var _ in connection.SubscribeAsync<string>(subject, cancellationToken: cts.Token)
                               .WithCancellation(cts.Token))
            {
                Interlocked.Increment(ref count);
            }
        }, CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await Task.Delay(250);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await action();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await Task.Delay(listenFor);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await cts.CancelAsync();
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await subscription;
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (OperationCanceledException)
        {
        }

        return count;
    }

    private static async Task<long> CountStreamMessagesAsync(string natsUrl, string streamName)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true
        });
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var stream = await jetStream.GetStreamAsync(streamName);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await stream.RefreshAsync();
        return stream.Info.State.Messages;
    }

    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountDispatchedAsync(string connectionString)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("select count(*) from alarm_outbox where dispatched_at_utc is not null", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static async Task<string> SingleEventTypeAsync(string connectionString) =>
        (await EventTypesAsync(connectionString)).Single();

    private static async Task<IReadOnlyCollection<string>> EventTypesAsync(string connectionString)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("select event_type from alarm_outbox order by occurred_at_utc", connection);
        var results = new List<string>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync();
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
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

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class NoOpOutboxSignal : IAlarmOutboxSignal
    {
        public void Kick()
        {
        }
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class FakeRouteServer(WebApplication app) : IAsyncDisposable
    {
        public string BaseAddress { get; } = app.Urls.Single();

        public static async Task<FakeRouteServer> StartAsync()
        {
            var port = GetFreePort();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build();

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
            app.MapGet("/internal/v1/units/{unitId:guid}/route", (Guid unitId) =>
                unitId == UnitId
                    ? Results.Ok(new UnitRouteDto(TenantId, SiteId, UnitId, "demo-site", "chp-001", "Combined Heat and Power Unit 001"))
                    : Results.NotFound());

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
            app.MapGet("/internal/v1/tenants/{tenantId:guid}", (Guid tenantId) =>
                tenantId == TenantId
                    ? Results.Ok(new TenantDto(TenantId, "demo-operator", "Demo Operator", "EU"))
                    : Results.NotFound());

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.StartAsync();
            return new FakeRouteServer(app);
        }

        public async ValueTask DisposeAsync()
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.StopAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
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
