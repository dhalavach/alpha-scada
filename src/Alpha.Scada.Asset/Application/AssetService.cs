using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Wolverine;

namespace Alpha.Scada.Asset.Application;

public sealed class AssetService(AssetRepository repository, TenantKeyResolver tenantKeyResolver, IMessageBus bus)
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

        var unit = await repository.SetUnitOnlineAsync(unitId, cancellationToken);
        if (unit is not null)
        {
            await PublishStatusChangedAsync(unit, route, cancellationToken);
        }
    }

    public Task SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken) =>
        SetUnitOnlineAsync(unitId, null, cancellationToken);

    public async Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken)
    {
        var changed = await repository.MarkStaleUnitsOfflineAsync(minutes, cancellationToken);
        foreach (var changedUnit in changed)
        {
            var tenantKey = await tenantKeyResolver.ResolveAsync(changedUnit.Unit.TenantId, cancellationToken);
            await PublishStatusChangedAsync(
                changedUnit.Unit,
                new UnitStatusRoute(tenantKey, changedUnit.SiteKey, changedUnit.Unit.Key),
                cancellationToken);
        }

        return changed.Select(unit => unit.Unit).ToArray();
    }

    public Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken) =>
        repository.GetStaleUnitsAsync(minutes, cancellationToken);

    private Task PublishStatusChangedAsync(UnitDto unit, UnitStatusRoute route, CancellationToken cancellationToken) =>
        bus.PublishAsync(new UnitStatusChanged(
            unit.TenantId,
            unit.SiteId,
            unit.Id,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            unit.Name,
            unit.Status,
            DateTimeOffset.UtcNow,
            unit.LastSeenUtc)).AsTask();
}
