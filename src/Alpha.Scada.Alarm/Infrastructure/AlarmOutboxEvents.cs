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
