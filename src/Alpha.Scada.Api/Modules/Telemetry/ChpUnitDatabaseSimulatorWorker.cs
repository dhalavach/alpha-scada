using Alpha.Scada.Api.Contracts;
using Alpha.Scada.Api.Data;

namespace Alpha.Scada.Api.Modules.Telemetry;

public sealed class ChpUnitDatabaseSimulatorWorker(
    IConfiguration configuration,
    PlatformRepository repository,
    ILogger<ChpUnitDatabaseSimulatorWorker> logger) : BackgroundService
{
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Simulator:Enabled", true))
        {
            logger.LogInformation("Combined heat and power unit simulator is disabled by configuration.");
            return;
        }

        logger.LogInformation("Starting database-backed combined heat and power unit simulator.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var wave = Math.Sin(now.ToUnixTimeSeconds() / 30.0);
            var envelope = new EdgeTelemetryEnvelope(
                "1.0",
                "chp-demo-001",
                now,
                CreateSamples(now, wave));

            await repository.IngestTelemetryAsync("demo-operator", "demo-energy-site", "chp-demo-001", envelope, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
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
}
