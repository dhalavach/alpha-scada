using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Wolverine;

namespace Alpha.Scada.Asset.Application;

public sealed class AssetService(AssetRepository repository, TenantKeyResolver tenantKeyResolver, IMessageBus messageBus)
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

    public async Task SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken)
    {
        var unit = await repository.SetUnitOnlineAsync(unitId, cancellationToken);
        if (unit is not null)
        {
            await PublishStatusChangedAsync(unit, cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken)
    {
        var units = await repository.MarkStaleUnitsOfflineAsync(minutes, cancellationToken);
        foreach (var unit in units)
        {
            await PublishStatusChangedAsync(unit, cancellationToken);
        }

        return units;
    }

    public Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken) =>
        repository.GetStaleUnitsAsync(minutes, cancellationToken);

    private async Task PublishStatusChangedAsync(UnitDto unit, CancellationToken cancellationToken)
    {
        var tenantKey = await tenantKeyResolver.ResolveAsync(unit.TenantId, cancellationToken);
        var route = await repository.GetUnitRouteAsync(unit.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Unit route {unit.Id} could not be resolved.");

        await messageBus.PublishAsync(new UnitStatusChanged(
            unit.TenantId,
            unit.SiteId,
            unit.Id,
            tenantKey,
            route.SiteKey,
            unit.Key,
            unit.Name,
            unit.Status,
            DateTimeOffset.UtcNow,
            unit.LastSeenUtc));
    }
}
