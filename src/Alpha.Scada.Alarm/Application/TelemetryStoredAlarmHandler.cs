using Alpha.Scada.Alarm.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Contracts;

namespace Alpha.Scada.Alarm.Application;

public sealed class TelemetryStoredAlarmHandler(
    ThresholdCache thresholds,
    AlarmService service)
{
    public async Task Handle(TelemetryBatchStored message, CancellationToken cancellationToken)
    {
        var tags = await thresholds.ResolveAsync(message.TenantId, message.UnitId, message.Samples, cancellationToken);
        var samples = message.Samples
            .Where(sample => tags.ContainsKey(sample.TagKey))
            .Select(sample =>
            {
                var tag = tags[sample.TagKey];
                return new ResolvedTelemetrySample(
                    sample.TagId,
                    sample.TagKey,
                    tag.Name,
                    tag.Subsystem,
                    tag.EngineeringUnit,
                    tag.AlarmLow,
                    tag.AlarmHigh,
                    sample.Value,
                    sample.Quality,
                    sample.SourceTimestampUtc);
            })
            .ToArray();

        if (samples.Length == 0)
        {
            return;
        }

        await service.EvaluateAsync(new AlarmEvaluationRequest(
            message.TenantId,
            message.UnitId,
            message.UnitKey,
            samples), cancellationToken);
    }
}
