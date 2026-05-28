using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Asset.Application;

public sealed class AssetService(AssetRepository repository)
{
    public Task<IReadOnlyCollection<SiteDto>> GetSitesAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetSitesAsync(user, cancellationToken);

    public Task<IReadOnlyCollection<UnitDto>> GetUnitsForSiteAsync(Guid siteId, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetUnitsForSiteAsync(siteId, user, cancellationToken);

    public Task<UnitDto?> GetUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetUnitAsync(unitId, user, cancellationToken);

    public Task<ResolvedUnitDto?> ResolveUnitAsync(Guid tenantId, string siteKey, string unitKey, CancellationToken cancellationToken) =>
        repository.ResolveUnitAsync(tenantId, siteKey, unitKey, cancellationToken);

    public Task<UnitRouteDto?> GetUnitRouteAsync(Guid unitId, CancellationToken cancellationToken) =>
        repository.GetUnitRouteAsync(unitId, cancellationToken);

    public Task SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken) =>
        repository.SetUnitOnlineAsync(unitId, cancellationToken);

    public Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken) =>
        repository.MarkStaleUnitsOfflineAsync(minutes, cancellationToken);

    public Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken) =>
        repository.GetStaleUnitsAsync(minutes, cancellationToken);
}
