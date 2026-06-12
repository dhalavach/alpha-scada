using System.Net;
using System.Net.Sockets;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Wolverine;
using Wolverine.Nats;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class CommunicationLossAlarmTests(PostgresContainerFixture postgres)
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Offline_unit_status_raises_communication_lost_alarm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-comm-loss-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "communication-loss NATS integration test");
            await using (nats)
            await using (var catalog = await FakeRouteServer.StartAsync())
            {
                var connectionString = await postgres.CreateDatabaseAsync(nameof(CommunicationLossAlarmTests));
                await WaitForPostgresAsync(connectionString);

                var natsUrl = NatsTestSupport.Url(nats);
                var alarmSubject = Topics.AlarmRaisedEvent;
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, alarmSubject, TimeSpan.FromSeconds(10));

                using var host = BuildAlarmHost(
                    connectionString,
                    natsUrl,
                    catalog.BaseAddress);

                await host.Services.GetRequiredService<AlarmMigrator>().MigrateAsync(CancellationToken.None);
                await host.StartAsync();
                await Task.Delay(250);

                await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(
                    new UnitStatusChanged(
                        TenantId,
                        SiteId,
                        UnitId,
                        "demo-operator",
                        "demo-site",
                        "chp-001",
                        "Combined Heat and Power Unit 001",
                        "offline",
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(5))));

                Assert.Equal(alarmSubject, await received);
                Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));

                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Online_unit_status_clears_communication_lost_alarm_and_rearms_next_outage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-comm-loss-clear-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var nats = await ContainerSupport.StartOrSkipAsync(
                () => NatsTestSupport.StartAsync(tempDir),
                "communication-loss clear NATS integration test");
            await using (nats)
            await using (var catalog = await FakeRouteServer.StartAsync())
            {
                var connectionString = await postgres.CreateDatabaseAsync(nameof(CommunicationLossAlarmTests));
                await WaitForPostgresAsync(connectionString);

                var natsUrl = NatsTestSupport.Url(nats);
                using var host = BuildAlarmHost(
                    connectionString,
                    natsUrl,
                    catalog.BaseAddress);

                await host.Services.GetRequiredService<AlarmMigrator>().MigrateAsync(CancellationToken.None);
                await host.StartAsync();
                await Task.Delay(250);

                await PublishStatusAsync(host, natsUrl, "offline", Topics.AlarmRaisedEvent);
                await PublishStatusAsync(host, natsUrl, "online", Topics.AlarmClearedEvent);

                Assert.Equal(1, await CountCommunicationLossAsync(connectionString, "cleared"));
                Assert.Equal(0, await CountOpenCommunicationLossAsync(connectionString));

                await PublishStatusAsync(host, natsUrl, "offline", Topics.AlarmRaisedEvent);
                await PublishStatusAsync(host, natsUrl, "online", Topics.AlarmClearedEvent);

                Assert.Equal(2, await CountCommunicationLossAsync(connectionString, "cleared"));
                Assert.Equal(0, await CountOpenCommunicationLossAsync(connectionString));

                await host.StopAsync();
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static IHost BuildAlarmHost(string connectionString, string natsUrl, string routeBaseAddress)
    {
        var settings = TestJwt.Settings(
            ("ConnectionStrings:Postgres", connectionString),
            ("Nats:Url", natsUrl),
            ("Services:Asset", routeBaseAddress),
            ("Services:Tenant", routeBaseAddress));

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services.AddServiceDatabase(context.Configuration);
                services.AddSingleton<AlarmMigrator>();
                services.AddSingleton<AlarmRepository>();
                services.AddSingleton<AlarmService>();
                services.AddSingleton<AlarmOutboxMetrics>();
                services.AddSingleton<AlarmOutboxDispatcher>();
                services.AddSingleton<IAlarmOutboxSignal>(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
                services.AddHostedService(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
                services.AddSingleton<UnitKeyResolver>();
                services.AddMemoryCache();
                services.AddAlphaServiceClients(
                    context.Configuration,
                    AlphaServiceClients.Asset,
                    AlphaServiceClients.Tenant);
            })
            .UseAlphaMessaging("comm-loss-test", options =>
            {
                options.Discovery.IncludeAssembly(typeof(UnitStatusAlarmHandler).Assembly);
                options.PublishMessage<UnitStatusChanged>().ToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream);
                options.PublishOutboxRelayedDomainEvent<AlarmRaised>(Topics.AlarmRaisedEvent);
                options.PublishOutboxRelayedDomainEvent<AlarmCleared>(Topics.AlarmClearedEvent);
                options.ListenToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream, "comm-loss-test-status");
            })
            .Build();
    }

    private static async Task PublishStatusAsync(IHost host, string natsUrl, string status, string expectedAlarmSubject)
    {
        var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, expectedAlarmSubject, TimeSpan.FromSeconds(10));
        await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(StatusChanged(status));
        Assert.Equal(expectedAlarmSubject, await received);
    }

    private static UnitStatusChanged StatusChanged(string status) =>
        new(
            TenantId,
            SiteId,
            UnitId,
            "demo-operator",
            "demo-site",
            "chp-001",
            "Combined Heat and Power Unit 001",
            status,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(5)));

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

    private static async Task<int> CountCommunicationLossAsync(string connectionString, string state)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from alarm_events where tag_id is null and state = @state",
            connection);
        command.Parameters.AddWithValue("state", state);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountOpenCommunicationLossAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from alarm_events where tag_id is null and state in ('active', 'acknowledged')",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
