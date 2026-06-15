using System.Text.Json;
using System.Reflection;
using JasperFx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Nats;
using Wolverine.Postgresql;

namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class AlphaMessaging
{
    public static IHostBuilder UseAlphaMessaging(
        this IHostBuilder host,
        string serviceName,
        Action<WolverineOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        IConfiguration? configuration = null;
        host.ConfigureServices((context, _) => configuration = context.Configuration);

        return host.UseWolverine(options =>
        {
            Configure(options, configuration ?? BuildFallbackConfiguration(), serviceName, configure);
        });
    }

    private static void Configure(
        WolverineOptions options,
        IConfiguration configuration,
        string serviceName,
        Action<WolverineOptions>? configure)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required to enable Alpha messaging.");
        var storageSchema = configuration.GetValue("Wolverine:StorageSchema", "wolverine");

        options.ServiceName = serviceName;
        options.Metrics.Mode = WolverineMetricsMode.SystemDiagnosticsMeter;
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            options.ApplicationAssembly = entryAssembly;
            options.Discovery.IncludeAssembly(entryAssembly);
        }

        options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
        options.UseSystemTextJsonForSerialization(json =>
        {
            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            json.PropertyNamingPolicy = web.PropertyNamingPolicy;
            json.DictionaryKeyPolicy = web.DictionaryKeyPolicy;
            json.PropertyNameCaseInsensitive = web.PropertyNameCaseInsensitive;
            json.DefaultIgnoreCondition = web.DefaultIgnoreCondition;
            json.NumberHandling = web.NumberHandling;
        });

        options.PersistMessagesWithPostgresql(postgres, storageSchema);
        ConfigureNats(options, configuration);

        options.Policies.AutoApplyTransactions();
        options.Policies.UseDurableInboxOnAllListeners();
        options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        options.Policies.AllSenders(sender => sender.CustomizeOutgoing(AddNatsDeduplicationHeader));
        options.OnAnyException()
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30))
            .Then.MoveToErrorQueue();

        configure?.Invoke(options);
    }

    private static void AddNatsDeduplicationHeader(Envelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.DeduplicationId)
            || envelope.Headers.ContainsKey(RawTelemetryHeaders.NatsMessageId))
        {
            return;
        }

        envelope.Headers[RawTelemetryHeaders.NatsMessageId] = envelope.DeduplicationId;
    }

    private static void ConfigureNats(WolverineOptions options, IConfiguration configuration)
    {
        var natsOptions = NatsOptions.FromConfiguration(configuration);

        var nats = options.UseNats(natsOptions.Url);
        if (!string.IsNullOrWhiteSpace(natsOptions.User))
        {
            nats.WithCredentials(natsOptions.User, natsOptions.Password ?? string.Empty);
        }

        nats.UseJetStream(streams =>
        {
            streams.MaxAge = TimeSpan.FromDays(7);
            streams.AckWait = TimeSpan.FromSeconds(30);
            streams.MaxDeliver = 5;
            streams.DuplicateWindow = TimeSpan.FromMinutes(10);
        });

        nats.DefineLogStream(
            Topics.EdgeStream,
            TimeSpan.FromDays(7),
            Topics.TelemetryWildcard,
            Topics.SparkplugWildcard);

        nats.DefineLogStream(
            Topics.DomainStream,
            TimeSpan.FromDays(7),
            Topics.TelemetryStoredEvent,
            Topics.StatusChangedEvent,
            Topics.AlarmRaisedEvent,
            Topics.AlarmClearedEvent,
            Topics.AlarmAcknowledgedEvent);

        nats.DefineLogStream(
            Topics.ReportsStream,
            TimeSpan.FromDays(7),
            Topics.ReportCompleted);

        nats.DefineWorkQueueStream(Topics.JobsStream, Topics.ReportRequested);

        nats.DefineLogStream(
            Topics.DlqStream,
            TimeSpan.FromDays(30),
            Topics.DlqWildcard);
    }

    private static IConfiguration BuildFallbackConfiguration() =>
        new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
}
