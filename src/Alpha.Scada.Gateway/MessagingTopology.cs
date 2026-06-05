/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Gateway/MessagingTopology.cs
- Module role: Alpha.Scada.Gateway is the public boundary/BFF. It keeps the React UI talking to one API surface, owns SignalR realtime fan-out, and translates browser-facing requests into calls or messages for backend services.
- Local role: This file declares how Wolverine routes messages to or from NATS subjects. It is intentionally small so messaging policy is visible and reviewable.
- Architecture connection: this file participates in the service boundary. Follow the dependency direction from HTTP/message edges inward to application/domain code and outward to infrastructure.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Alarm.Contracts;
using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Reporting.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Alpha.Scada.Telemetry.Contracts;
using Wolverine;

namespace Alpha.Scada.Gateway;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class MessagingTopology
{
    public static void Configure(WolverineOptions options)
    {
        options.PublishReportRequest<ReportRequested>();
        options.ListenForDomainEvent(Topics.ReportCompleted, "gateway-report-completed");
        options.ListenForDomainEvent(Topics.AlarmRaisedEvent, "gateway-alarm-raised");
        options.ListenForDomainEvent(Topics.AlarmClearedEvent, "gateway-alarm-cleared");
        options.ListenForDomainEvent(Topics.AlarmAcknowledgedEvent, "gateway-alarm-acknowledged");
        options.ListenForDomainEvent(Topics.StatusChangedEvent, "gateway-status");
        options.ListenForDomainEvent(Topics.TelemetryStoredEvent, "gateway-telemetry-stored");
    }
}
