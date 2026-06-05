/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/CommunicationLossAlarmTests.cs
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

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class CommunicationLossAlarmTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Offline_unit_status_raises_communication_lost_alarm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-comm-loss-test-{Guid.NewGuid():N}");
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
                throw SkipException.ForSkip($"Docker is not available for communication-loss integration test: {ex.Message}");
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (nats)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (var catalog = await FakeRouteServer.StartAsync())
            {
                var connectionString =
                    $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await WaitForPostgresAsync(connectionString);

                var natsUrl = NatsTestSupport.Url(nats);
                var alarmSubject = Topics.AlarmRaisedEvent;
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, alarmSubject, TimeSpan.FromSeconds(10));

                using var host = BuildAlarmHost(
                    connectionString,
                    natsUrl,
                    catalog.BaseAddress);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.Services.GetRequiredService<AlarmMigrator>().MigrateAsync(CancellationToken.None);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.StartAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(250);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
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

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(alarmSubject, await received);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
                Assert.Equal(1, await CountRowsAsync(connectionString, "alarm_events"));

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
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
            .UseAlphaMessaging("comm-loss-test", options =>
            {
                options.Discovery.IncludeAssembly(typeof(UnitStatusAlarmHandler).Assembly);
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<UnitStatusChanged>().ToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream);
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<AlarmRaised>().ToNatsSubject(Topics.AlarmRaisedEvent).UseJetStream(Topics.DomainStream);
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.ListenToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream, "comm-loss-test-status");
            })
            .Build();
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
