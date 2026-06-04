using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults.Messaging;
using Npgsql;

namespace Alpha.Scada.Asset.Application;

public sealed class AssetService(
    AssetRepository repository,
    TenantKeyResolver tenantKeyResolver,
    NpgsqlDataSource dataSource,
    WolverineTransactionalOutbox outbox)
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

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var unit = await repository.SetUnitOnlineAsync(connection, transaction, unitId, cancellationToken);
        WolverineOutboxBatch? outboxBatch = null;
        if (unit is not null)
        {
            outboxBatch = await StoreStatusChangedAsync(connection, transaction, unit, route, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (outboxBatch is not null)
        {
            await outbox.PublishAndClearAsync(outboxBatch, cancellationToken);
        }
    }

    public Task SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken) =>
        SetUnitOnlineAsync(unitId, null, cancellationToken);

    public async Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = await repository.MarkStaleUnitsOfflineAsync(connection, transaction, minutes, cancellationToken);
        var outboxBatches = new List<WolverineOutboxBatch>();
        foreach (var changedUnit in changed)
        {
            var tenantKey = await tenantKeyResolver.ResolveAsync(changedUnit.Unit.TenantId, cancellationToken);
            outboxBatches.Add(await StoreStatusChangedAsync(
                connection,
                transaction,
                changedUnit.Unit,
                new UnitStatusRoute(tenantKey, changedUnit.SiteKey, changedUnit.Unit.Key),
                cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        foreach (var outboxBatch in outboxBatches)
        {
            await outbox.PublishAndClearAsync(outboxBatch, cancellationToken);
        }

        return changed.Select(unit => unit.Unit).ToArray();
    }

    public Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken) =>
        repository.GetStaleUnitsAsync(minutes, cancellationToken);

    private Task<WolverineOutboxBatch> StoreStatusChangedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        UnitDto unit,
        UnitStatusRoute route,
        CancellationToken cancellationToken) =>
        outbox.StoreAsync(connection, transaction, new UnitStatusChanged(
            unit.TenantId,
            unit.SiteId,
            unit.Id,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            unit.Name,
            unit.Status,
            DateTimeOffset.UtcNow,
            unit.LastSeenUtc), cancellationToken);
}
