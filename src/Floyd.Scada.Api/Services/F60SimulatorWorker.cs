using Floyd.Scada.Api.Models;

namespace Floyd.Scada.Api.Services;

public sealed class F60SimulatorWorker(
    ScadaStore store,
    ILogger<F60SimulatorWorker> logger) : BackgroundService
{
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting F60 simulator worker.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var seconds = now.ToUnixTimeSeconds();
            var wave = Math.Sin(seconds / 30.0);

            var samples = store.GetDefinitions()
                .Select(definition => CreateSample(definition, now, wave))
                .ToArray();

            await store.IngestAsync(samples);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private TagSample CreateSample(TagDefinition definition, DateTimeOffset timestamp, double wave)
    {
        var value = definition.Key switch
        {
            "fuel.wood_chip_feed_kg_h" => Around(55, 4, wave),
            "gasifier.reactor_temp_c" => Around(780, 35, wave),
            "gas_cleaning.filter_dp_mbar" => Around(42, 8, wave),
            "engine.electrical_output_kw" => Around(58, 3, wave),
            "engine.oil_reservoir_l" => Around(24, 0.15, -wave),
            "heat.thermal_output_kw" => Around(134, 7, wave),
            "heat.supply_temp_c" => Around(88, 3, wave),
            "heat.return_temp_c" => Around(55, 3, -wave),
            "biochar.output_m3_day" => Around(0.62, 0.08, wave),
            "exhaust.temperature_c" => Around(132, 6, wave),
            "air.compressed_pressure_bar" => Around(8.1, 0.25, wave),
            "ventilation.air_exchange_m3_h" => Around(820, 25, wave),
            "safety.negative_pressure_pa" => Around(-120, 18, wave),
            "safety.co_ppm" => Around(8, 3, wave),
            "safety.fire_suppression_ready" => 1,
            _ => Around(0, 1, wave)
        };

        return new TagSample(
            TagKey: definition.Key,
            Name: definition.Name,
            Subsystem: definition.Subsystem,
            Value: Math.Round(value, 2),
            EngineeringUnit: definition.EngineeringUnit,
            Quality: "good",
            TimestampUtc: timestamp);
    }

    private double Around(double center, double spread, double wave)
    {
        var noise = (_random.NextDouble() - 0.5) * spread;
        return center + (spread * 0.6 * wave) + noise;
    }
}
