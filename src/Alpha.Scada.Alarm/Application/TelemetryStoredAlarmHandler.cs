/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/TelemetryStoredAlarmHandler.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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
