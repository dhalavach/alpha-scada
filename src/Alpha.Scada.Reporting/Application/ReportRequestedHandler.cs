/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Application/ReportRequestedHandler.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Reporting.Contracts;

namespace Alpha.Scada.Reporting.Application;

public sealed class ReportRequestedHandler(ReportingService service)
{
    public async Task<ReportCompleted> Handle(ReportRequested message, CancellationToken cancellationToken)
    {
        var report = await service.RunQueuedMonthlyAsync(
            message.TenantId,
            message.UnitId,
            message.Period,
            cancellationToken);

        return new ReportCompleted(
            message.RequestId,
            report.Id,
            report.TenantId,
            report.UnitId,
            report.Period,
            DateTimeOffset.UtcNow,
            message.CorrelationId);
    }
}
