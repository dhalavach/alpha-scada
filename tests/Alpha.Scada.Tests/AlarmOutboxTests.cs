/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/AlarmOutboxTests.cs
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
using Microsoft.Extensions.Logging.Abstractions;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.Core;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NATS.Client.JetStream;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using NpgsqlTypes;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine.Nats;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AlarmOutboxTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TagId = Guid.Parse("40000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UserId = Guid.Parse("50000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Alarm_evaluation_inserts_outbox_row_in_same_transaction_and_dedupes_reprocessing()
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var postgres = await StartPostgresAsync();
// LEARN: branches only when the boolean condition is true.
        if (postgres is null)
        {
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var postgres = await StartPostgresAsync();
// LEARN: branches only when the boolean condition is true.
        if (postgres is null)
        {
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var service = services.GetRequiredService<AlarmService>();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var raised = await service.EvaluateAsync(Request(Breaching(TagId)), CancellationToken.None);

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await service.AcknowledgeAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                raised.Raised.Single().AlarmId,
// LEARN: creates a new object or record instance.
                new CurrentUserDto(UserId, TenantId, "operator@alpha.local", "Operator", "Operator"),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
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
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tempDir = Path.Combine(Path.GetTempPath(), $"alpha-alarm-outbox-test-{Guid.NewGuid():N}");
// LEARN: executes one C# statement; semicolons terminate most statements.
        Directory.CreateDirectory(tempDir);
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var postgres = await StartPostgresAsync();
// LEARN: branches only when the boolean condition is true.
            if (postgres is null)
            {
// LEARN: returns a value or exits the current method.
                return;
            }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using (postgres)
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var nats = await StartNatsAsync(tempDir);
// LEARN: branches only when the boolean condition is true.
                if (nats is null)
                {
// LEARN: returns a value or exits the current method.
                    return;
                }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                await using (nats)
                {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                    var connectionString = ConnectionString(postgres);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await WaitForPostgresAsync(connectionString);

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                    await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await new AlarmMigrator(dataSource, NullLogger<AlarmMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                    var outboxId = await InsertOutboxAsync(connectionString, RaisedEvent());

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
                    using var host = BuildDispatcherHost(connectionString, NatsTestSupport.Url(nats));
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                    await host.StartAsync();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                    var dispatcher = host.Services.GetRequiredService<AlarmOutboxDispatcher>();

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                    var firstCount = await CountSubjectMessagesAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
                        NatsTestSupport.Url(nats),
// LEARN: continues an argument/object/collection initializer onto the next line.
                        Topics.AlarmRaisedEvent,
// LEARN: continues an argument/object/collection initializer onto the next line.
                        TimeSpan.FromSeconds(2),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
            Directory.Delete(tempDir, recursive: true);
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static ServiceProvider BuildServiceProvider(string connectionString, string routeBaseAddress)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var settings = new Dictionary<string, string?>
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["ConnectionStrings:Postgres"] = connectionString,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["Services:Asset"] = routeBaseAddress,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ["Services:Tenant"] = routeBaseAddress
        };
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var services = new ServiceCollection();
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<IConfiguration>(configuration);
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddServiceDatabase(configuration);
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<AlarmRepository>();
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<AlarmService>();
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<UnitKeyResolver>();
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddSingleton<IAlarmOutboxSignal, NoOpOutboxSignal>();
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddMemoryCache();
// LEARN: executes one C# statement; semicolons terminate most statements.
        services.AddAlphaServiceClients(configuration, AlphaServiceClients.Asset, AlphaServiceClients.Tenant);
// LEARN: returns a value or exits the current method.
        return services.BuildServiceProvider();
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static IHost BuildDispatcherHost(string connectionString, string natsUrl)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var settings = new Dictionary<string, string?>
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["ConnectionStrings:Postgres"] = connectionString,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["Nats:Url"] = natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
            ["AlarmOutbox:BatchSize"] = "10",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ["AlarmOutbox:MaxAttempts"] = "3"
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
                services.AddSingleton<AlarmOutboxDispatcher>();
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
            .UseAlphaMessaging("alarm-outbox-dispatcher-test", options =>
            {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
                options.PublishMessage<AlarmRaised>().ToNatsSubject(Topics.AlarmRaisedEvent).UseJetStream(Topics.DomainStream);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static AlarmEvaluationRequest Request(params ResolvedTelemetrySample[] samples) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(TenantId, UnitId, "Combined Heat and Power Unit 001", samples);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static ResolvedTelemetrySample Breaching(Guid tagId) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(tagId, "engine.electrical_output_kw", "Electrical Output", "Engine", "kW", 0, 100, 150, "good", DateTimeOffset.UtcNow);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static AlarmRaised RaisedEvent() =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(Guid.NewGuid(), TenantId, UnitId, TagId, "demo-operator", "demo-site", "chp-001", "warning", "Electrical Output above high threshold", DateTimeOffset.UtcNow);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<IContainer?> StartPostgresAsync()
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        IContainer? postgres = null;
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: creates a new object or record instance.
            postgres = new ContainerBuilder()
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
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.StartAsync();
// LEARN: returns a value or exits the current method.
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

// LEARN: returns a value or exits the current method.
            return null;
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<IContainer?> StartNatsAsync(string tempDir)
    {
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: returns a value or exits the current method.
            return await NatsTestSupport.StartAsync(tempDir);
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (DockerUnavailableException)
        {
// LEARN: returns a value or exits the current method.
            return null;
        }
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static string ConnectionString(IContainer postgres) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<Guid> InsertOutboxAsync(string connectionString, object message)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var serialized = AlarmOutboxEvents.Serialize(message);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("id", id);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("event_type", serialized.EventType);
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = serialized.Payload });
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync();
// LEARN: returns a value or exits the current method.
        return id;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("id", outboxId);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync();
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<int> CountSubjectMessagesAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
        string natsUrl,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string subject,
// LEARN: continues an argument/object/collection initializer onto the next line.
        TimeSpan listenFor,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        Func<Task> action)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = natsUrl,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            RetryOnInitialConnect = true
// LEARN: executes one C# statement; semicolons terminate most statements.
        });

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var count = 0;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
        using var cts = new CancellationTokenSource();
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var subscription = Task.Run(async () =>
        {
// LEARN: asynchronously loops over a stream of values without blocking a thread.
            await foreach (var _ in connection.SubscribeAsync<string>(subject, cancellationToken: cts.Token)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                               .WithCancellation(cts.Token))
            {
// LEARN: executes one C# statement; semicolons terminate most statements.
                Interlocked.Increment(ref count);
            }
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
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

// LEARN: returns a value or exits the current method.
        return count;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<long> CountStreamMessagesAsync(string natsUrl, string streamName)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(new NatsOpts
        {
// LEARN: continues an argument/object/collection initializer onto the next line.
            Url = natsUrl,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            RetryOnInitialConnect = true
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var stream = await jetStream.GetStreamAsync(streamName);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await stream.RefreshAsync();
// LEARN: returns a value or exits the current method.
        return stream.Info.State.Messages;
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
    private static async Task<int> CountDispatchedAsync(string connectionString)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("select count(*) from alarm_outbox where dispatched_at_utc is not null", connection);
// LEARN: returns a value or exits the current method.
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static async Task<string> SingleEventTypeAsync(string connectionString) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        (await EventTypesAsync(connectionString)).Single();

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<IReadOnlyCollection<string>> EventTypesAsync(string connectionString)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("select event_type from alarm_outbox order by occurred_at_utc", connection);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var results = new List<string>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync();
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync())
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            results.Add(reader.GetString(0));
        }

// LEARN: returns a value or exits the current method.
        return results;
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

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class NoOpOutboxSignal : IAlarmOutboxSignal
    {
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
        public void Kick()
        {
        }
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
