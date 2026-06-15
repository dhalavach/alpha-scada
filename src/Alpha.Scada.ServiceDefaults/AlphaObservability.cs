using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Alpha.Scada.ServiceDefaults;

public static class AlphaObservability
{
    public const string ServiceDefaultsMeterName = "Alpha.Scada.ServiceDefaults";
    public const string TelemetryMeterName = "Alpha.Scada.Telemetry";
    public const string AlarmMeterName = "Alpha.Scada.Alarm";
    public const string WolverineInstrumentationName = "Wolverine";

    private static readonly double[] IngestionDurationBuckets =
        [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    public static WebApplicationBuilder AddAlphaObservability(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Services.AddSingleton(new AlphaServiceIdentity(serviceName));
        builder.Services.AddSingleton<WolverineQueueMetrics>();
        builder.Services.AddHostedService<WolverineQueueMetricsSampler>();

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(
                    ServiceDefaultsMeterName,
                    TelemetryMeterName,
                    AlarmMeterName,
                    WolverineInstrumentationName)
                .AddView(
                    "alpha.scada.telemetry.ingestion.processing",
                    new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = IngestionDurationBuckets
                    })
                .AddPrometheusExporter())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource(
                    WolverineInstrumentationName,
                    TelemetryMeterName,
                    AlarmMeterName));

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            openTelemetry.WithTracing(tracing =>
                tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)));
        }

        return builder;
    }
}

public sealed record AlphaServiceIdentity(string Name);

public sealed class WolverineQueueMetrics
{
    private static readonly Meter Meter = new(AlphaObservability.ServiceDefaultsMeterName);
    private readonly KeyValuePair<string, object?> serviceTag;
    private readonly ObservableGauge<long> errorQueueGauge;
    private readonly ObservableGauge<long> outboxGauge;
    private long errorQueueDepth;
    private long outboxDepth;

    public WolverineQueueMetrics(AlphaServiceIdentity service)
    {
        serviceTag = new KeyValuePair<string, object?>("service", service.Name);
        errorQueueGauge = Meter.CreateObservableGauge(
            "alpha.scada.wolverine.error_queue.depth",
            () => new Measurement<long>(Volatile.Read(ref errorQueueDepth), serviceTag),
            description: "Dead-lettered Wolverine envelopes awaiting operator action");
        outboxGauge = Meter.CreateObservableGauge(
            "alpha.scada.wolverine.outbox.depth",
            () => new Measurement<long>(Volatile.Read(ref outboxDepth), serviceTag),
            description: "Wolverine outgoing envelopes awaiting delivery");
    }

    public void Update(long errors, long outgoing)
    {
        Interlocked.Exchange(ref errorQueueDepth, errors);
        Interlocked.Exchange(ref outboxDepth, outgoing);
    }
}

public sealed class WolverineQueueMetricsSampler(
    NpgsqlDataSource dataSource,
    WolverineQueueMetrics metrics,
    ILogger<WolverineQueueMetricsSampler> logger) : BackgroundService
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SampleAsync(stoppingToken);
        using var timer = new PeriodicTimer(SamplingInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SampleAsync(stoppingToken);
        }
    }

    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var errors = await CountIfPresentAsync(
                connection,
                "wolverine.wolverine_dead_letters",
                "select count(*) from wolverine.wolverine_dead_letters",
                cancellationToken);
            var outgoing = await CountIfPresentAsync(
                connection,
                "wolverine.wolverine_outgoing_envelopes",
                "select count(*) from wolverine.wolverine_outgoing_envelopes",
                cancellationToken);
            metrics.Update(errors, outgoing);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            logger.LogWarning(exception, "Unable to sample Wolverine queue metrics.");
        }
    }

    private static async Task<long> CountIfPresentAsync(
        NpgsqlConnection connection,
        string tableName,
        string countSql,
        CancellationToken cancellationToken)
    {
        await using var exists = new NpgsqlCommand("select to_regclass(@table_name) is not null", connection);
        exists.Parameters.AddWithValue("table_name", tableName);
        if (await exists.ExecuteScalarAsync(cancellationToken) is not true)
        {
            return 0;
        }

        await using var count = new NpgsqlCommand(countSql, connection);
        return Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken));
    }
}
