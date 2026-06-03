namespace Alpha.Scada.ServiceDefaults.Messaging;

/// <summary>
/// Cross-service JSON body shape for raw edge interop messages that are not
/// produced by Wolverine. Wolverine-native NATS messages use Wolverine's own
/// envelope and durable inbox/outbox metadata.
/// </summary>
public sealed record MessageEnvelope<T>(
    Guid MessageId,
    Guid? CorrelationId,
    Guid? CausationId,
    Guid? TenantId,
    string SchemaVersion,
    DateTimeOffset OccurredAtUtc,
    T Payload);
