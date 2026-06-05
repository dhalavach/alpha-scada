/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Application/TelemetryStoredAlarmHandler.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Contracts;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Alarm.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryStoredAlarmHandler(
// LEARN: continues an argument/object/collection initializer onto the next line.
    ThresholdCache thresholds,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    AlarmService service)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Handle(TelemetryBatchStored message, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tags = await thresholds.ResolveAsync(message.TenantId, message.UnitId, message.Samples, cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var samples = message.Samples
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .Where(sample => tags.ContainsKey(sample.TagKey))
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            .Select(sample =>
            {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
                var tag = tags[sample.TagKey];
// LEARN: returns a value or exits the current method.
                return new ResolvedTelemetrySample(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    sample.TagId,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    sample.TagKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.Name,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.Subsystem,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.EngineeringUnit,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.AlarmLow,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    tag.AlarmHigh,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    sample.Value,
// LEARN: continues an argument/object/collection initializer onto the next line.
                    sample.Quality,
// LEARN: executes one C# statement; semicolons terminate most statements.
                    sample.SourceTimestampUtc);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            })
// LEARN: executes one C# statement; semicolons terminate most statements.
            .ToArray();

// LEARN: branches only when the boolean condition is true.
        if (samples.Length == 0)
        {
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await service.EvaluateAsync(new AlarmEvaluationRequest(
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.UnitKey,
// LEARN: executes one C# statement; semicolons terminate most statements.
            samples), cancellationToken);
    }
}
