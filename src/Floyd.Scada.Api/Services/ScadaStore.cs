using System.Collections.Concurrent;
using Floyd.Scada.Api.Models;

namespace Floyd.Scada.Api.Services;

public sealed class ScadaStore
{
    private const int MaxSamplesPerTag = 3_600;
    private static readonly DateTimeOffset BootTimeUtc = DateTimeOffset.UtcNow;

    private readonly object _sync = new();
    private readonly Dictionary<string, TagDefinition> _definitions = BuildDefinitions();
    private readonly ConcurrentDictionary<string, TagSample> _current = new();
    private readonly Dictionary<string, Queue<TagSample>> _history = new();
    private readonly ConcurrentDictionary<string, AlarmEvent> _activeAlarms = new();
    private int _alarmCounter;
    private DateTimeOffset _lastSeenUtc = BootTimeUtc;

    public event Func<IReadOnlyCollection<TagSample>, Task>? SamplesChanged;

    public IReadOnlyCollection<UnitSummary> GetUnits()
    {
        return
        [
            new UnitSummary(
                UnitId: "f60-demo-001",
                Name: "F60 Demo Unit",
                Model: "FLOYD CHP F60",
                SiteName: "Demo Site",
                State: GetUnitState(),
                LastSeenUtc: _lastSeenUtc)
        ];
    }

    public IReadOnlyCollection<TagSample> GetCurrentTags()
    {
        return _current.Values
            .OrderBy(tag => tag.Subsystem)
            .ThenBy(tag => tag.Name)
            .ToArray();
    }

    public IReadOnlyCollection<TagSample> GetHistory(string tagKey, TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(window);

        lock (_sync)
        {
            if (!_history.TryGetValue(tagKey, out var samples))
            {
                return [];
            }

            return samples
                .Where(sample => sample.TimestampUtc >= cutoff)
                .OrderBy(sample => sample.TimestampUtc)
                .ToArray();
        }
    }

    public IReadOnlyCollection<AlarmEvent> GetActiveAlarms()
    {
        return _activeAlarms.Values
            .Where(alarm => alarm.Active)
            .OrderByDescending(alarm => alarm.RaisedAtUtc)
            .ToArray();
    }

    public MonthlyReportSummary GetMonthlyReport()
    {
        var now = DateTimeOffset.UtcNow;
        var runtimeHours = Math.Max(0.1, now.Subtract(BootTimeUtc).TotalHours);
        var electricalKw = GetCurrentValue("engine.electrical_output_kw", 56);
        var thermalKw = GetCurrentValue("heat.thermal_output_kw", 132);
        var woodChipsKgPerHour = GetCurrentValue("fuel.wood_chip_feed_kg_h", 55);
        var availability = GetActiveAlarms().Any(alarm => alarm.Severity == "critical") ? 96.5 : 99.1;

        return new MonthlyReportSummary(
            UnitId: "f60-demo-001",
            Period: now.ToString("yyyy-MM"),
            ElectricalKwh: Math.Round(electricalKw * runtimeHours, 1),
            ThermalKwh: Math.Round(thermalKw * runtimeHours, 1),
            RuntimeHours: Math.Round(runtimeHours, 1),
            AvailabilityPercent: availability,
            EstimatedWoodChipsKg: Math.Round(woodChipsKgPerHour * runtimeHours, 1),
            EstimatedBiocharM3: Math.Round(woodChipsKgPerHour * runtimeHours * 0.00045, 2),
            AlarmCount: GetActiveAlarms().Count);
    }

    public async Task IngestAsync(IReadOnlyCollection<TagSample> samples)
    {
        if (samples.Count == 0)
        {
            return;
        }

        _lastSeenUtc = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            foreach (var sample in samples)
            {
                _current[sample.TagKey] = sample;

                if (!_history.TryGetValue(sample.TagKey, out var tagHistory))
                {
                    tagHistory = new Queue<TagSample>();
                    _history[sample.TagKey] = tagHistory;
                }

                tagHistory.Enqueue(sample);

                while (tagHistory.Count > MaxSamplesPerTag)
                {
                    tagHistory.Dequeue();
                }

                EvaluateAlarm(sample);
            }
        }

        if (SamplesChanged is not null)
        {
            await SamplesChanged.Invoke(samples);
        }
    }

    public IReadOnlyCollection<TagDefinition> GetDefinitions()
    {
        return _definitions.Values.ToArray();
    }

    private string GetUnitState()
    {
        if (DateTimeOffset.UtcNow.Subtract(_lastSeenUtc) > TimeSpan.FromSeconds(15))
        {
            return "offline";
        }

        return GetActiveAlarms().Any(alarm => alarm.Severity == "critical") ? "alarm" : "running";
    }

    private double GetCurrentValue(string tagKey, double fallback)
    {
        return _current.TryGetValue(tagKey, out var sample) ? sample.Value : fallback;
    }

    private void EvaluateAlarm(TagSample sample)
    {
        if (!_definitions.TryGetValue(sample.TagKey, out var definition))
        {
            return;
        }

        var alarmKey = $"threshold:{sample.TagKey}";
        var triggered = false;
        var message = string.Empty;

        if (definition.AlarmLow is not null && sample.Value < definition.AlarmLow.Value)
        {
            triggered = true;
            message = $"{definition.Name} below {definition.AlarmLow.Value:0.##} {definition.EngineeringUnit}";
        }
        else if (definition.AlarmHigh is not null && sample.Value > definition.AlarmHigh.Value)
        {
            triggered = true;
            message = $"{definition.Name} above {definition.AlarmHigh.Value:0.##} {definition.EngineeringUnit}";
        }

        if (!triggered)
        {
            _activeAlarms.TryRemove(alarmKey, out _);
            return;
        }

        _activeAlarms.GetOrAdd(alarmKey, _ =>
        {
            var severity = sample.Subsystem.Equals("Safety", StringComparison.OrdinalIgnoreCase)
                ? "critical"
                : "warning";

            return new AlarmEvent(
                Id: Interlocked.Increment(ref _alarmCounter).ToString("D6"),
                TagKey: sample.TagKey,
                Name: definition.Name,
                Severity: severity,
                Message: message,
                RaisedAtUtc: sample.TimestampUtc,
                Active: true);
        });
    }

    private static Dictionary<string, TagDefinition> BuildDefinitions()
    {
        TagDefinition[] definitions =
        [
            new("fuel.wood_chip_feed_kg_h", "Wood chip feed", "Fuel Feed", "kg/h", 35, 75),
            new("gasifier.reactor_temp_c", "Gasifier reactor temperature", "Gasifier", "degC", 650, 950),
            new("gas_cleaning.filter_dp_mbar", "Filter differential pressure", "Gas Cleaning", "mbar", null, 85),
            new("engine.electrical_output_kw", "Electrical output", "Engine / Generator", "kW", 45, 65),
            new("engine.oil_reservoir_l", "Engine oil reservoir", "Engine / Generator", "L", 5, null),
            new("heat.thermal_output_kw", "Thermal output", "Heat Recovery", "kW", 90, 150),
            new("heat.supply_temp_c", "Hot water supply temperature", "Heat Recovery", "degC", 75, 98),
            new("heat.return_temp_c", "Cold water return temperature", "Heat Recovery", "degC", 42, 68),
            new("biochar.output_m3_day", "Biochar production estimate", "Biochar", "m3/day", 0.2, 1.0),
            new("exhaust.temperature_c", "Exhaust temperature", "Exhaust", "degC", null, 150),
            new("air.compressed_pressure_bar", "Compressed air pressure", "Compressed Air", "bar", 7.2, 9.2),
            new("ventilation.air_exchange_m3_h", "Air exchange", "Ventilation", "m3/h", 750, null),
            new("safety.negative_pressure_pa", "Negative pressure", "Safety", "Pa", -250, -20),
            new("safety.co_ppm", "CO concentration", "Safety", "ppm", null, 30),
            new("safety.fire_suppression_ready", "Fire suppression ready", "Safety", "state", 0.5, null)
        ];

        return definitions.ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);
    }
}
