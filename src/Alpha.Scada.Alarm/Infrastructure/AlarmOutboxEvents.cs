/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Infrastructure/AlarmOutboxEvents.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using System.Text.Json;
using Alpha.Scada.Alarm.Contracts;

namespace Alpha.Scada.Alarm.Infrastructure;

public static class AlarmOutboxEvents
{
    public const string AlarmRaisedType = "alarm.raised";
    public const string AlarmClearedType = "alarm.cleared";
    public const string AlarmAcknowledgedType = "alarm.acknowledged";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AlarmOutboxMessage Serialize(object message)
    {
        var eventType = message switch
        {
            AlarmRaised => AlarmRaisedType,
            AlarmCleared => AlarmClearedType,
            AlarmAcknowledged => AlarmAcknowledgedType,
            _ => throw new InvalidOperationException($"Unsupported alarm outbox message type '{message.GetType().Name}'.")
        };

        return new AlarmOutboxMessage(eventType, JsonSerializer.Serialize(message, message.GetType(), JsonOptions));
    }

    public static object Deserialize(string eventType, string payload) =>
        eventType switch
        {
            AlarmRaisedType => JsonSerializer.Deserialize<AlarmRaised>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Alarm raised payload was empty."),
            AlarmClearedType => JsonSerializer.Deserialize<AlarmCleared>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Alarm cleared payload was empty."),
            AlarmAcknowledgedType => JsonSerializer.Deserialize<AlarmAcknowledged>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Alarm acknowledged payload was empty."),
            _ => throw new InvalidOperationException($"Unsupported alarm outbox event type '{eventType}'.")
        };
}

public sealed record AlarmOutboxMessage(string EventType, string Payload);
