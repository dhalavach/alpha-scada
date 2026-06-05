/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessaging.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Reflection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using JasperFx;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Hosting;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine.ErrorHandling;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine.Nats;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine.Postgresql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlphaMessaging
{
// LEARN: uses the Alpha messaging convention wrapper around Wolverine and NATS.
    public static IHostBuilder UseAlphaMessaging(
// LEARN: continues an argument/object/collection initializer onto the next line.
        this IHostBuilder host,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string serviceName,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        Action<WolverineOptions>? configure = null)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

// LEARN: executes one C# statement; semicolons terminate most statements.
        IConfiguration? configuration = null;
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        host.ConfigureServices((context, _) => configuration = context.Configuration);

// LEARN: returns a value or exits the current method.
        return host.UseWolverine(options =>
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            Configure(options, configuration ?? BuildFallbackConfiguration(), serviceName, configure);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static void Configure(
// LEARN: continues an argument/object/collection initializer onto the next line.
        WolverineOptions options,
// LEARN: continues an argument/object/collection initializer onto the next line.
        IConfiguration configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
        string serviceName,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        Action<WolverineOptions>? configure)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var postgres = configuration.GetConnectionString("Postgres")
// LEARN: creates a new object or record instance.
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required to enable Alpha messaging.");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var storageSchema = configuration.GetValue("Wolverine:StorageSchema", "wolverine");

// LEARN: executes one C# statement; semicolons terminate most statements.
        options.ServiceName = serviceName;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var entryAssembly = Assembly.GetEntryAssembly();
// LEARN: branches only when the boolean condition is true.
        if (entryAssembly is not null)
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            options.ApplicationAssembly = entryAssembly;
// LEARN: executes one C# statement; semicolons terminate most statements.
            options.Discovery.IncludeAssembly(entryAssembly);
        }

// LEARN: executes one C# statement; semicolons terminate most statements.
        options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        options.UseSystemTextJsonForSerialization(json =>
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
// LEARN: executes one C# statement; semicolons terminate most statements.
            json.PropertyNamingPolicy = web.PropertyNamingPolicy;
// LEARN: executes one C# statement; semicolons terminate most statements.
            json.DictionaryKeyPolicy = web.DictionaryKeyPolicy;
// LEARN: executes one C# statement; semicolons terminate most statements.
            json.PropertyNameCaseInsensitive = web.PropertyNameCaseInsensitive;
// LEARN: executes one C# statement; semicolons terminate most statements.
            json.DefaultIgnoreCondition = web.DefaultIgnoreCondition;
// LEARN: executes one C# statement; semicolons terminate most statements.
            json.NumberHandling = web.NumberHandling;
// LEARN: executes one C# statement; semicolons terminate most statements.
        });

// LEARN: executes one C# statement; semicolons terminate most statements.
        options.PersistMessagesWithPostgresql(postgres, storageSchema);
// LEARN: executes one C# statement; semicolons terminate most statements.
        ConfigureNats(options, configuration);

// LEARN: executes one C# statement; semicolons terminate most statements.
        options.Policies.AutoApplyTransactions();
// LEARN: executes one C# statement; semicolons terminate most statements.
        options.Policies.UseDurableInboxOnAllListeners();
// LEARN: executes one C# statement; semicolons terminate most statements.
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        options.Policies.AllSenders(sender => sender.CustomizeOutgoing(AddNatsDeduplicationHeader));
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        options.OnAnyException()
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30))
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Then.MoveToErrorQueue();

// LEARN: executes one C# statement; semicolons terminate most statements.
        configure?.Invoke(options);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static void AddNatsDeduplicationHeader(Envelope envelope)
    {
// LEARN: branches only when the boolean condition is true.
        if (string.IsNullOrWhiteSpace(envelope.DeduplicationId)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            || envelope.Headers.ContainsKey(RawTelemetryHeaders.NatsMessageId))
        {
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: executes one C# statement; semicolons terminate most statements.
        envelope.Headers[RawTelemetryHeaders.NatsMessageId] = envelope.DeduplicationId;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static void ConfigureNats(WolverineOptions options, IConfiguration configuration)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var natsOptions = NatsOptions.FromConfiguration(configuration);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var nats = options.UseNats(natsOptions.Url);
// LEARN: branches only when the boolean condition is true.
        if (!string.IsNullOrWhiteSpace(natsOptions.User))
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            nats.WithCredentials(natsOptions.User, natsOptions.Password ?? string.Empty);
        }

// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        nats.UseJetStream(streams =>
        {
// LEARN: executes one C# statement; semicolons terminate most statements.
            streams.MaxAge = TimeSpan.FromDays(7);
// LEARN: executes one C# statement; semicolons terminate most statements.
            streams.AckWait = TimeSpan.FromSeconds(30);
// LEARN: executes one C# statement; semicolons terminate most statements.
            streams.MaxDeliver = 5;
// LEARN: executes one C# statement; semicolons terminate most statements.
            streams.DuplicateWindow = TimeSpan.FromMinutes(10);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });

// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        nats.DefineLogStream(
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.EdgeStream,
// LEARN: continues an argument/object/collection initializer onto the next line.
            TimeSpan.FromDays(7),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.TelemetryWildcard,
// LEARN: executes one C# statement; semicolons terminate most statements.
            Topics.SparkplugWildcard);

// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        nats.DefineLogStream(
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.DomainStream,
// LEARN: continues an argument/object/collection initializer onto the next line.
            TimeSpan.FromDays(7),
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.TelemetryStoredEvent,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.StatusChangedEvent,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.AlarmRaisedEvent,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.AlarmClearedEvent,
// LEARN: continues an argument/object/collection initializer onto the next line.
            Topics.AlarmAcknowledgedEvent,
// LEARN: executes one C# statement; semicolons terminate most statements.
            Topics.ReportCompleted);

// LEARN: executes one C# statement; semicolons terminate most statements.
        nats.DefineWorkQueueStream(Topics.JobsStream, Topics.ReportRequested);
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static IConfiguration BuildFallbackConfiguration() =>
// LEARN: creates a new object or record instance.
        new ConfigurationBuilder()
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .AddEnvironmentVariables()
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Build();
}
