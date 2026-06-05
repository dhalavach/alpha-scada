/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/MessagingTopology.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file declares how Wolverine routes messages to or from NATS subjects. It is intentionally small so messaging policy is visible and reviewable.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults.Messaging;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Telemetry.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Telemetry;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class MessagingTopology
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static void Configure(WolverineOptions options)
    {
// LEARN: configures Wolverine to publish this domain event to a NATS subject.
        options.PublishDomainEvent<TelemetryBatchStored>(Topics.TelemetryStoredEvent);
    }
}
