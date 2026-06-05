/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Telemetry/Application/TelemetryService.cs
- Module role: Alpha.Scada.Telemetry is the historian and normalization boundary. It converts raw edge payloads into canonical readings, resolves tags, persists current/history rows, and emits domain events after storage.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Infrastructure;

namespace Alpha.Scada.Telemetry.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TelemetryService(TelemetryRepository repository)
{
// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task IngestAsync(TelemetryIngestRequest request, CancellationToken cancellationToken) =>
        repository.IngestAsync(request, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<TagValueDto>> GetCurrentAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetCurrentAsync(unitId, user, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<TelemetryHistoryPointDto>> GetHistoryAsync(Guid tagId, TimeSpan window, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetHistoryAsync(tagId, window, user, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<ReportAggregateDto> GetReportAggregateAsync(Guid unitId, ReportAggregateRequest request, CancellationToken cancellationToken) =>
        repository.GetReportAggregateAsync(unitId, request, cancellationToken);
}
