using System.Net;
using System.Net.Sockets;
using Alpha.Scada.Alarm.Application;
using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Asset.Contracts;
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
using Npgsql;
using Wolverine;
using Wolverine.Nats;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class CommunicationLossAlarmTests
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
            var postgres = new ContainerBuilder()
                .WithImage("postgres:16-alpine")
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
                throw SkipException.ForSkip($"Docker is not available for communication-loss integration test: {ex.Message}");
            }

            await using (postgres)
            await using (nats)
            await using (var catalog = await FakeRouteServer.StartAsync())
            {
                var connectionString =
                    $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
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

    private static IHost BuildAlarmHost(string connectionString, string natsUrl, string routeBaseAddress)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Nats:Url"] = natsUrl,
            ["Services:Asset"] = routeBaseAddress,
            ["Services:Tenant"] = routeBaseAddress
        };

        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services.AddServiceDatabase(context.Configuration);
                services.AddSingleton<AlarmMigrator>();
                services.AddSingleton<AlarmRepository>();
                services.AddSingleton<AlarmService>();
                services.AddSingleton<UnitKeyResolver>();
                services.AddMemoryCache();
                services.AddHttpClient("asset", client => client.BaseAddress = new Uri(routeBaseAddress));
                services.AddHttpClient("tenant", client => client.BaseAddress = new Uri(routeBaseAddress));
            })
            .UseAlphaMessaging("comm-loss-test", options =>
            {
                options.Discovery.IncludeAssembly(typeof(UnitStatusAlarmHandler).Assembly);
                options.PublishMessage<UnitStatusChanged>().ToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream);
                options.PublishMessage<AlarmRaised>().ToNatsSubject(Topics.AlarmRaisedEvent).UseJetStream(Topics.DomainStream);
                options.ListenToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream, "comm-loss-test-status");
            })
            .Build();
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
