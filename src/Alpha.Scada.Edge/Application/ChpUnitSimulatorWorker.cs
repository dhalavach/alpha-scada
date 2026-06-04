using System.Security.Cryptography;
using System.Text.Json;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Alpha.Scada.Edge.Application;

public sealed class ChpUnitSimulatorWorker(
    IConfiguration configuration,
    ILogger<ChpUnitSimulatorWorker> logger) : BackgroundService
{
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Simulator:Enabled", true))
        {
            logger.LogInformation("Combined heat and power unit simulator is disabled.");
            return;
        }

        await using var connection = new NatsConnection(BuildNatsOptions());
        var jetStream = new NatsJSContextFactory().CreateContext(connection);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var subject = Topics.Telemetry("demo-operator", "demo-energy-site", "chp-demo-001");

        logger.LogInformation("Starting edge telemetry simulator.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var wave = Math.Sin(now.ToUnixTimeSeconds() / 30.0);
                var envelope = new EdgeTelemetryEnvelope("1.0", "chp-demo-001", now, CreateSamples(now, wave));
                var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, jsonOptions);
                var headers = new NatsHeaders
                {
                    [RawTelemetryHeaderNames.NatsMessageId] = DeterministicMessageId(subject, payload)
                };

                await jetStream.PublishAsync(subject, payload, headers: headers, cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Simulator telemetry publish failed. Retrying.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private NatsOpts BuildNatsOptions()
    {
        var user = configuration["Simulator:NatsUser"] ?? configuration["Nats:User"];
        var password = configuration["Simulator:NatsPassword"] ?? configuration["Nats:Password"];
        return new NatsOpts
        {
            Url = configuration["Nats:Url"] ?? "nats://localhost:4222",
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

internal static class RawTelemetryHeaderNames
{
    public const string NatsMessageId = "Nats-Msg-Id";
}
