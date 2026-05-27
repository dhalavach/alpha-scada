using Alpha.Scada.Contracts;
using Alpha.Scada.TagCatalog.Infrastructure;

namespace Alpha.Scada.TagCatalog.Application;

public sealed class TagCatalogService(TagCatalogRepository repository)
{
    public Task<IReadOnlyCollection<TagDto>> GetTagsForUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetTagsForUnitAsync(unitId, user, cancellationToken);

    public Task<IReadOnlyCollection<TagDto>> ResolveTagsAsync(ResolveTagsRequest request, CancellationToken cancellationToken) =>
        repository.ResolveTagsAsync(request, cancellationToken);
}
