/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/MessageEnvelope.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file mostly defines DTOs/messages. C# records are used here because cross-boundary payloads should be immutable, comparable value shapes.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults.Messaging;

/// <summary>
/// Cross-service JSON body shape for raw edge interop messages that are not
/// produced by Wolverine. Wolverine-native NATS messages use Wolverine's own
/// envelope and durable inbox/outbox metadata.
/// </summary>
// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record MessageEnvelope<T>(
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid MessageId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid? CorrelationId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid? CausationId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    Guid? TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
    string SchemaVersion,
// LEARN: continues an argument/object/collection initializer onto the next line.
    DateTimeOffset OccurredAtUtc,
// LEARN: executes one C# statement; semicolons terminate most statements.
    T Payload);
