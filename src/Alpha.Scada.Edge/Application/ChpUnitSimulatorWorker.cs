/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Edge/Application/ChpUnitSimulatorWorker.cs
- Module role: Alpha.Scada.Edge is the edge/simulator service. In this codebase it stands in for field-side publishers by producing raw telemetry onto NATS subjects.
- Local role: This file is a hosted background process. ASP.NET Core starts it with the service and stops it with the host cancellation token.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: BackgroundService is the .NET hosted-worker base class; ExecuteAsync runs until the host cancellation token is signaled. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Security.Cryptography;
using System.Text.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Alpha.Scada.Edge.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ChpUnitSimulatorWorker(
    IConfiguration configuration,
// LEARN: inherits from BackgroundService, the ASP.NET Core base type for long-running hosted workers.
    ILogger<ChpUnitSimulatorWorker> logger) : BackgroundService
{
    private readonly Random _random = new();

// LEARN: overrides the hosted-worker entry point; the host calls this when the service starts.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
// LEARN: branches only when the boolean condition is true.
        if (!configuration.GetValue("Simulator:Enabled", true))
        {
// LEARN: writes structured log output; placeholders become searchable log fields.
            logger.LogInformation("Combined heat and power unit simulator is disabled.");
            return;
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NatsConnection(BuildNatsOptions());
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var subject = Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001");

// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogInformation("Starting edge telemetry simulator.");
// LEARN: starts a loop that continues while its condition remains true.
        while (!stoppingToken.IsCancellationRequested)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
                var now = DateTimeOffset.UtcNow;
                var wave = Math.Sin(now.ToUnixTimeSeconds() / 30.0);
                var envelope = new EdgeTelemetryEnvelope("1.0", "chp-demo-001", now, CreateSamples(now, wave));
                var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, jsonOptions);
                var messageId = DeterministicMessageId(subject, payload);
                var headers = new NatsHeaders
                {
                    [RawTelemetryHeaders.NatsMessageId] = messageId
                };

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await jetStream.PublishAsync(subject, payload, headers: headers, cancellationToken: stoppingToken);
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch (Exception ex)
            {
// LEARN: writes structured log output; placeholders become searchable log fields.
                logger.LogWarning(ex, "Simulator telemetry publish failed. Retrying.");
            }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private NatsOpts BuildNatsOptions()
    {
        var options = NatsOptions.FromConfiguration(configuration);
        var user = configuration["Simulator:NatsUser"] ?? configuration["Nats:User"];
        var password = configuration["Simulator:NatsPassword"] ?? configuration["Nats:Password"];
        return new NatsOpts
        {
            Url = options.Url,
            Name = $"alpha-scada-simulator-{Environment.MachineName}",
            RetryOnInitialConnect = true,
            AuthOpts = string.IsNullOrWhiteSpace(user)
                ? new NatsAuthOpts()
                : new NatsAuthOpts
                {
                    Username = user,
                    Password = password ?? string.Empty
                }
        };
    }

    private IReadOnlyCollection<EdgeTelemetrySample> CreateSamples(DateTimeOffset timestamp, double wave)
    {
        return
        [
            Sample("fuel.wood_chip_feed_kg_h", 55, 4, wave, timestamp),
            Sample("gasifier.reactor_temp_c", 780, 35, wave, timestamp),
            Sample("gas_cleaning.filter_dp_mbar", 42, 8, wave, timestamp),
            Sample("engine.electrical_output_kw", 58, 3, wave, timestamp),
            Sample("engine.oil_reservoir_l", 24, 0.15, -wave, timestamp),
            Sample("heat.thermal_output_kw", 134, 7, wave, timestamp),
            Sample("heat.supply_temp_c", 88, 3, wave, timestamp),
            Sample("heat.return_temp_c", 55, 3, -wave, timestamp),
            Sample("biochar.output_m3_day", 0.62, 0.08, wave, timestamp),
            Sample("exhaust.temperature_c", 132, 6, wave, timestamp),
            Sample("air.compressed_pressure_bar", 8.1, 0.25, wave, timestamp),
            Sample("ventilation.air_exchange_m3_h", 820, 25, wave, timestamp),
            Sample("safety.negative_pressure_pa", -120, 18, wave, timestamp),
            Sample("safety.co_ppm", 8, 3, wave, timestamp),
            new("safety.fire_suppression_ready", 1, "good", timestamp)
        ];
    }

    private EdgeTelemetrySample Sample(string key, double center, double spread, double wave, DateTimeOffset timestamp)
    {
        var noise = (_random.NextDouble() - 0.5) * spread;
        var value = center + (spread * 0.6 * wave) + noise;
        return new EdgeTelemetrySample(key, Math.Round(value, 2), "good", timestamp);
    }

    private static string DeterministicMessageId(string subject, ReadOnlySpan<byte> payload)
    {
        var subjectBytes = System.Text.Encoding.UTF8.GetBytes(subject);
        var buffer = new byte[subjectBytes.Length + 1 + payload.Length];
        subjectBytes.CopyTo(buffer);
        buffer[subjectBytes.Length] = 0x1f;
        payload.CopyTo(buffer.AsSpan(subjectBytes.Length + 1));
        var hash = SHA256.HashData(buffer);
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }
}
