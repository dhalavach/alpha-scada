using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;

namespace Alpha.Scada.Asset.Application;

public sealed class AssetService(AssetRepository repository, TenantKeyResolver tenantKeyResolver)
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

    public async Task SetUnitOnlineAsync(Guid unitId, UnitStatusRoute? knownRoute, CancellationToken cancellationToken)
    {
        var route = knownRoute;
        if (route is null)
        {
            var unitRoute = await repository.GetUnitRouteAsync(unitId, cancellationToken)
                ?? throw new InvalidOperationException($"Unit route {unitId} could not be resolved.");
            var tenantKey = await tenantKeyResolver.ResolveAsync(unitRoute.TenantId, cancellationToken);
            route = new UnitStatusRoute(tenantKey, unitRoute.SiteKey, unitRoute.UnitKey);
        }

        await repository.SetUnitOnlineAsync(unitId, route, cancellationToken);
    }

    public Task SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken) =>
        SetUnitOnlineAsync(unitId, null, cancellationToken);

    public async Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken)
    {
        var candidates = await repository.GetStaleUnitsAsync(minutes, cancellationToken);
        var tenantKeys = new Dictionary<Guid, string>();
        foreach (var tenantId in candidates.Select(unit => unit.TenantId).Distinct())
        {
            tenantKeys[tenantId] = await tenantKeyResolver.ResolveAsync(tenantId, cancellationToken);
        }

        return await repository.MarkStaleUnitsOfflineAsync(minutes, tenantKeys, cancellationToken);
    }

    public Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken) =>
        repository.GetStaleUnitsAsync(minutes, cancellationToken);

}
