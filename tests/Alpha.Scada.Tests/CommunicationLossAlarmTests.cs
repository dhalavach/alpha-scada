/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/CommunicationLossAlarmTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Net.Sockets;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
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
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine.Nats;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class CommunicationLossAlarmTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Offline_unit_status_raises_communication_lost_alarm()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-comm-loss-test-{Guid.NewGuid():N}");
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
                throw SkipException.ForSkip($"Docker is not available for communication-loss integration test: {ex.Message}");
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (nats)
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (var catalog = await FakeRouteServer.StartAsync())
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
                var alarmSubject = Topics.AlarmRaisedEvent;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var received = NatsTestSupport.WaitForSubjectAsync(natsUrl, alarmSubject, TimeSpan.FromSeconds(10));

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
                using var host = BuildAlarmHost(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    connectionString,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    natsUrl,
// LEARN: executes one C# statement; semicolons terminate most statements.
                    catalog.BaseAddress);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.Services.GetRequiredService<AlarmMigrator>().MigrateAsync(CancellationToken.None);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.StartAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(250);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await host.Services.GetRequiredService<Wolverine.IMessageBus>().PublishAsync(
// LEARN: creates a new object or record instance.
                    new UnitStatusChanged(
// LEARN: continues an argument/object/collection initializer onto the next line.
                        TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        SiteId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "demo-operator",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "demo-site",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "chp-001",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "Combined Heat and Power Unit 001",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        "offline",
// LEARN: continues an argument/object/collection initializer onto the next line.
                        DateTimeOffset.UtcNow,
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
            Directory.Delete(tempDir, recursive: true);
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static IHost BuildAlarmHost(string connectionString, string natsUrl, string routeBaseAddress)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var settings = new Dictionary<string, string?>
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["ConnectionStrings:Postgres"] = connectionString,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["Nats:Url"] = natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["Services:Asset"] = routeBaseAddress,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ["Services:Tenant"] = routeBaseAddress
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
                services.AddSingleton<AlarmMigrator>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<AlarmRepository>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<AlarmService>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<AlarmOutboxDispatcher>();
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
                services.AddSingleton<IAlarmOutboxSignal>(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
                services.AddHostedService(provider => provider.GetRequiredService<AlarmOutboxDispatcher>());
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddSingleton<UnitKeyResolver>();
// LEARN: executes one C# statement; semicolons terminate most statements.
                services.AddMemoryCache();
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                services.AddAlphaServiceClients(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    context.Configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    AlphaServiceClients.Asset,
// LEARN: executes one C# statement; semicolons terminate most statements.
                    AlphaServiceClients.Tenant);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
            .UseAlphaMessaging("comm-loss-test", options =>
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                options.Discovery.IncludeAssembly(typeof(UnitStatusAlarmHandler).Assembly);
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<UnitStatusChanged>().ToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream);
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<AlarmRaised>().ToNatsSubject(Topics.AlarmRaisedEvent).UseJetStream(Topics.DomainStream);
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.ListenToNatsSubject(Topics.StatusChangedEvent).UseJetStream(Topics.DomainStream, "comm-loss-test-status");
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();
    }

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

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class FakeRouteServer(WebApplication app) : IAsyncDisposable
    {
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public string BaseAddress { get; } = app.Urls.Single();

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        public static async Task<FakeRouteServer> StartAsync()
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
            app.MapGet("/internal/v1/units/{unitId:guid}/route", (Guid unitId) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                unitId == UnitId
// LEARN: creates a new object or record instance.
                    ? Results.Ok(new UnitRouteDto(TenantId, SiteId, UnitId, "demo-site", "chp-001", "Combined Heat and Power Unit 001"))
// LEARN: executes one C# statement; semicolons terminate most statements.
                    : Results.NotFound());

// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
            app.MapGet("/internal/v1/tenants/{tenantId:guid}", (Guid tenantId) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                tenantId == TenantId
// LEARN: creates a new object or record instance.
                    ? Results.Ok(new TenantDto(TenantId, "demo-operator", "Demo Operator", "EU"))
// LEARN: executes one C# statement; semicolons terminate most statements.
                    : Results.NotFound());

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.StartAsync();
// LEARN: returns a value or exits the current method.
            return new FakeRouteServer(app);
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
