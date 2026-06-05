/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessagingTopology.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file declares how Wolverine routes messages to or from NATS subjects. It is intentionally small so messaging policy is visible and reviewable.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Wolverine.Nats;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults.Messaging;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class AlphaMessagingTopology
{
// LEARN: configures Wolverine to publish this domain event to a NATS subject.
    public static WolverineOptions PublishDomainEvent<T>(this WolverineOptions options, string subject)
    {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        options.PublishMessage<T>().ToNatsSubject(subject).UseJetStream(Topics.DomainStream);
// LEARN: returns a value or exits the current method.
        return options;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static WolverineOptions ListenForDomainEvent(this WolverineOptions options, string subject, string durableName)
    {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        options.ListenToNatsSubject(subject).UseJetStream(Topics.DomainStream, durableName);
// LEARN: returns a value or exits the current method.
        return options;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static WolverineOptions PublishReportRequest<T>(this WolverineOptions options)
    {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        options.PublishMessage<T>().ToNatsSubject(Topics.ReportRequested).UseJetStream(Topics.JobsStream);
// LEARN: returns a value or exits the current method.
        return options;
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static WolverineOptions ListenForReportRequests(this WolverineOptions options, string durableName)
    {
// LEARN: works with NATS/JetStream, the message broker and durable stream layer.
        options.ListenToNatsSubject(Topics.ReportRequested).UseJetStream(Topics.JobsStream, durableName);
// LEARN: returns a value or exits the current method.
        return options;
    }
}
