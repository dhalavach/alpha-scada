/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.TagCatalog/Application/TagCatalogService.cs
- Module role: Alpha.Scada.TagCatalog is the tag-catalog service. It owns tag definitions, engineering units, thresholds, subsystem grouping, and report ontology/configuration rather than scattering those constants through code.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.TagCatalog.Infrastructure;

namespace Alpha.Scada.TagCatalog.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TagCatalogService(TagCatalogRepository repository)
{
// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<TagDto>> GetTagsForUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetTagsForUnitAsync(unitId, user, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<TagDto>> ResolveTagsAsync(ResolveTagsRequest request, CancellationToken cancellationToken) =>
        repository.ResolveTagsAsync(request, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<ReportProfileDto?> GetReportProfileAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken) =>
        repository.GetReportProfileAsync(tenantId, unitId, cancellationToken);
}
