/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Application/ReportRequestedHandler.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file is a message handler. Wolverine discovers these classes and invokes them when a matching domain event or job arrives.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Contracts;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Reporting.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ReportRequestedHandler(ReportingService service)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<ReportCompleted> Handle(ReportRequested message, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var report = await service.RunQueuedMonthlyAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.Period,
// LEARN: executes one C# statement; semicolons terminate most statements.
            cancellationToken);

// LEARN: returns a value or exits the current method.
        return new ReportCompleted(
// LEARN: continues an argument/object/collection initializer onto the next line.
            message.RequestId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            report.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
            report.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            report.UnitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            report.Period,
// LEARN: continues an argument/object/collection initializer onto the next line.
            DateTimeOffset.UtcNow,
// LEARN: executes one C# statement; semicolons terminate most statements.
            message.CorrelationId);
    }
}
