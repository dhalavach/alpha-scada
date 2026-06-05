/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/MessagingTopology.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file declares how Wolverine routes messages to or from NATS subjects. It is intentionally small so messaging policy is visible and reviewable.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Wolverine;

namespace Alpha.Scada.Reporting;

public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.ListenForReportRequests("reporting-report-requested");
        options.PublishDomainEvent<ReportCompleted>(Topics.ReportCompleted);
    }
}
