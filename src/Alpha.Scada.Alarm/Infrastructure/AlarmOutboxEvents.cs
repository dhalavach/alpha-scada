/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Alarm/Infrastructure/AlarmOutboxEvents.cs
- Module role: Alpha.Scada.Alarm is the alarm service. It evaluates domain telemetry/status events against thresholds and quality rules, persists alarm lifecycle state, and publishes alarm changes.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text.Json;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Alarm.Contracts;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Alarm.Infrastructure;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlarmOutboxEvents
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string AlarmRaisedType = "alarm.raised";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string AlarmClearedType = "alarm.cleared";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string AlarmAcknowledgedType = "alarm.acknowledged";

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static AlarmOutboxMessage Serialize(object message)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var eventType = message switch
        {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmRaised => AlarmRaisedType,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmCleared => AlarmClearedType,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmAcknowledged => AlarmAcknowledgedType,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            _ => throw new InvalidOperationException($"Unsupported alarm outbox message type '{message.GetType().Name}'.")
        };

// LEARN: returns a value or exits the current method.
        return new AlarmOutboxMessage(eventType, JsonSerializer.Serialize(message, message.GetType(), JsonOptions));
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static object Deserialize(string eventType, string payload) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        eventType switch
        {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmRaisedType => JsonSerializer.Deserialize<AlarmRaised>(payload, JsonOptions)
// LEARN: creates a new object or record instance.
                ?? throw new InvalidOperationException("Alarm raised payload was empty."),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmClearedType => JsonSerializer.Deserialize<AlarmCleared>(payload, JsonOptions)
// LEARN: creates a new object or record instance.
                ?? throw new InvalidOperationException("Alarm cleared payload was empty."),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            AlarmAcknowledgedType => JsonSerializer.Deserialize<AlarmAcknowledged>(payload, JsonOptions)
// LEARN: creates a new object or record instance.
                ?? throw new InvalidOperationException("Alarm acknowledged payload was empty."),
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            _ => throw new InvalidOperationException($"Unsupported alarm outbox event type '{eventType}'.")
        };
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record AlarmOutboxMessage(string EventType, string Payload);
