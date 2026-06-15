using System.Net;
using System.Net.Sockets;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
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

[Collection(ContainerCollection.Name)]
public sealed class AlarmOutboxTests(PostgresContainerFixture postgres)
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Alarm_evaluation_inserts_outbox_row_in_same_transaction_and_dedupes_reprocessing()
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(AlarmOutboxTests));
        await WaitForPostgresAsync(connectionString);

        await using var routeServer = await FakeRouteServer.StartAsync();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);

        await using var services = BuildServiceProvider(connectionString, routeServer.BaseAddress);
        var service = services.GetRequiredService<AlarmService>();

        await service.EvaluateAsync(Request(Breaching(TagId)), CancellationToken.None);

        Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));
        Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_outbox"));
        Assert.Equal(AlarmOutboxEvents.AlarmRaisedType, await SingleEventTypeAsync(connectionString));

        await service.EvaluateAsync(Request(Breaching(TagId)), CancellationToken.None);

        Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));
        Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_outbox"));
    }

    [Fact]
    public async Task Alarm_acknowledgement_inserts_outbox_row()
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(AlarmOutboxTests));
        await WaitForPostgresAsync(connectionString);

        await using var routeServer = await FakeRouteServer.StartAsync();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);

        await using var services = BuildServiceProvider(connectionString, routeServer.BaseAddress);
        var service = services.GetRequiredService<AlarmService>();
        var raised = await service.EvaluateAsync(Request(Breaching(TagId)), CancellationToken.None);

        await service.AcknowledgeAsync(
            raised.Raised.Single().AlarmId,
            new CurrentUserDto(UserId, TenantId, "operator@alpha.local", "Operator", "Operator"),
            CancellationToken.None);

        Assert.Equal(2, await CountRowsAsync(connectionString, "alarm_outbox"));
        Assert.Contains(AlarmOutboxEvents.AlarmAcknowledgedType, await EventTypesAsync(connectionString));
    }

    [Fact]
    public async Task Dispatcher_publishes_pending_rows_marks_dispatched_and_uses_stable_dedup_id()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-alarm-outbox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "alarm outbox NATS integration test");
            await using (nats)
            {
                var connectionString = await postgres.CreateDatabaseAsync(nameof(AlarmOutboxTests));
                await WaitForPostgresAsync(connectionString);

                await using var dataSource = NpgsqlDataSource.Create(connectionString);
                await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
                var outboxId = await InsertOutboxAsync(connectionString, RaisedEvent());

                using var host = BuildDispatcherHost(connectionString, NatsTestSupport.Url(nats));
                await host.StartAsync();
                var dispatcher = host.Services.GetRequiredService<AlarmOutboxDispatcher>();

                var firstCount = await CountSubjectMessagesAsync(
                    NatsTestSupport.Url(nats),
                    Topics.AlarmRaisedEvent,
                    TimeSpan.FromSeconds(2),
                    async () => await dispatcher.DispatchPendingAsync(CancellationToken.None));

                Assert.Equal(1, firstCount);
                Assert.Equal(1, await CountDispatchedAsync(connectionString));
                Assert.Equal(1, await CountStreamMessagesAsync(NatsTestSupport.Url(nats), Topics.DomainStream));
                Assert.Equal(0, await CountWolverineOutgoingAsync(connectionString));

                await ResetOutboxRowAsync(connectionString, outboxId);
                await dispatcher.DispatchPendingAsync(CancellationToken.None);

                Assert.Equal(1, await CountStreamMessagesAsync(NatsTestSupport.Url(nats), Topics.DomainStream));
                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dispatcher_reclaims_stale_claims_prunes_old_rows_and_reports_poison_state()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-alarm-outbox-maintenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "alarm outbox maintenance integration test");
            await using (nats)
            {
                var connectionString = await postgres.CreateDatabaseAsync($"{nameof(AlarmOutboxTests)}Maintenance");
                await WaitForPostgresAsync(connectionString);

                await using var dataSource = NpgsqlDataSource.Create(connectionString);
                await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
                var staleClaimId = await InsertOutboxAsync(connectionString, RaisedEvent());
                await SetClaimedAtAsync(connectionString, staleClaimId, DateTimeOffset.UtcNow.AddMinutes(-5));
                await InsertPoisonAsync(connectionString, RaisedEvent(), attempts: 3);
                await InsertOldDispatchedAsync(connectionString, RaisedEvent());

                using var host = BuildDispatcherHost(connectionString, NatsTestSupport.Url(nats));
                await host.StartAsync();
                var dispatcher = host.Services.GetRequiredService<AlarmOutboxDispatcher>();
                await dispatcher.DispatchPendingAsync(CancellationToken.None);

                Assert.Equal(1, await CountDispatchedAsync(connectionString));
                Assert.Equal(0, await CountOldDispatchedAsync(connectionString));

                var metrics = host.Services.GetRequiredService<AlarmOutboxMetrics>();
                Assert.Equal(1, metrics.PendingCount);
                Assert.Equal(1, metrics.PoisonCount);
                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static ServiceProvider BuildServiceProvider(string connectionString, string routeBaseAddress)
    {
        var settings = TestJwt.Settings(
            ("ConnectionStrings:Postgres", connectionString),
            ("Services:Asset", routeBaseAddress),
            ("Services:Tenant", routeBaseAddress));
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
                services.AddSingleton<AlarmOutboxMetrics>();
                services.AddSingleton<AlarmOutboxDispatcher>();
            })
            .UseAlphaMessaging("alarm-outbox-dispatcher-test", options =>
            {
                options.PublishOutboxRelayedDomainEvent<AlarmRaised>(Topics.AlarmRaisedEvent);
            })
            .Build();
    }

    private static AlarmEvaluationRequest Request(params ResolvedTelemetrySample[] samples) =>
        new(TenantId, UnitId, "Combined Heat and Power Unit 001", samples);

    private static ResolvedTelemetrySample Breaching(Guid tagId) =>
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 150, "good", DateTimeOffset.UtcNow);

    private static AlarmRaised RaisedEvent() =>
        new(Guid.NewGuid(), TenantId, UnitId, TagId, "demo-operator", "demo-site", "chp-001", "warning", "Electrical Output above high threshold", DateTimeOffset.UtcNow);

    private static async Task<Guid> InsertOutboxAsync(string connectionString, object message)
    {
        var serialized = AlarmOutboxEvents.Serialize(message);
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            insert into alarm_outbox (id, event_type, payload)
            values (@id, @event_type, @payload)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("event_type", serialized.EventType);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = serialized.Payload });
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task ResetOutboxRowAsync(string connectionString, Guid outboxId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set dispatched_at_utc = null
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", outboxId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetClaimedAtAsync(string connectionString, Guid outboxId, DateTimeOffset claimedAt)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set claimed_at_utc = @claimed_at
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", outboxId);
        command.Parameters.AddWithValue("claimed_at", claimedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertPoisonAsync(string connectionString, object message, int attempts)
    {
        var id = await InsertOutboxAsync(connectionString, message);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set attempts = @attempts
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("attempts", attempts);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertOldDispatchedAsync(string connectionString, object message)
    {
        var id = await InsertOutboxAsync(connectionString, message);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update alarm_outbox
            set dispatched_at_utc = now() - interval '8 days'
            where id = @id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountSubjectMessagesAsync(
        string natsUrl,
        string subject,
        TimeSpan listenFor,
        Func<Task> action)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true
        });

        var count = 0;
        using var cts = new CancellationTokenSource();
        var subscription = Task.Run(async () =>
        {
            await foreach (var _ in connection.SubscribeAsync<string>(subject, cancellationToken: cts.Token)
                               .WithCancellation(cts.Token))
            {
                Interlocked.Increment(ref count);
            }
        }, CancellationToken.None);

        await Task.Delay(250);
        await action();
        await Task.Delay(listenFor);
        await cts.CancelAsync();
        try
        {
            await subscription;
        }
        catch (OperationCanceledException)
        {
        }

        return count;
    }

    private static async Task<long> CountStreamMessagesAsync(string natsUrl, string streamName)
    {
        await using var connection = new NatsConnection(new NatsOpts
        {
            Url = natsUrl,
            RetryOnInitialConnect = true
        });
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var stream = await jetStream.GetStreamAsync(streamName);
        await stream.RefreshAsync();
        return stream.Info.State.Messages;
    }

    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountDispatchedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*) from alarm_outbox where dispatched_at_utc is not null", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountOldDispatchedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            select count(*)
            from alarm_outbox
            where dispatched_at_utc < now() - interval '7 days'
            """, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountWolverineOutgoingAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            select case
                when to_regclass('wolverine.wolverine_outgoing_envelopes') is null then 0
                else (select count(*) from wolverine.wolverine_outgoing_envelopes)
            end
            """, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> SingleEventTypeAsync(string connectionString) =>
        (await EventTypesAsync(connectionString)).Single();

    private static async Task<IReadOnlyCollection<string>> EventTypesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select event_type from alarm_outbox order by occurred_at_utc", connection);
        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
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

    private sealed class NoOpOutboxSignal : IAlarmOutboxSignal
    {
        public void Kick()
        {
        }
    }

    private sealed class FakeRouteServer(WebApplication app) : IAsyncDisposable
    {
        public string BaseAddress { get; } = app.Urls.Single();

        public static async Task<FakeRouteServer> StartAsync()
        {
            var port = GetFreePort();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build();

            app.MapGet("/internal/v1/units/{unitId:guid}/route", (Guid unitId) =>
                unitId == UnitId
                    ? Results.Ok(new UnitRouteDto(TenantId, SiteId, UnitId, "demo-site", "chp-001", "Combined Heat and Power Unit 001"))
                    : Results.NotFound());

            app.MapGet("/internal/v1/tenants/{tenantId:guid}", (Guid tenantId) =>
                tenantId == TenantId
                    ? Results.Ok(new TenantDto(TenantId, "demo-operator", "Demo Operator", "EU"))
                    : Results.NotFound());

            await app.StartAsync();
            return new FakeRouteServer(app);
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
