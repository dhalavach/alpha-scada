/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Application/AssetService.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.Asset.Application;

public sealed class AssetService(
    AssetRepository repository,
    TenantKeyResolver tenantKeyResolver,
    NpgsqlDataSource dataSource)
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

    public async Task<UnitStatusChanged?> SetUnitOnlineAsync(Guid unitId, UnitStatusRoute? knownRoute, CancellationToken cancellationToken)
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
        await transaction.CommitAsync(cancellationToken);
        return unit is null ? null : ToStatusChanged(unit, route);
    }

    public Task<UnitStatusChanged?> SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken) =>
        SetUnitOnlineAsync(unitId, null, cancellationToken);

    public async Task<IReadOnlyCollection<UnitStatusChanged>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = await repository.MarkStaleUnitsOfflineAsync(connection, transaction, minutes, cancellationToken);
        var events = new List<UnitStatusChanged>();
        foreach (var changedUnit in changed)
        {
            var tenantKey = await tenantKeyResolver.ResolveAsync(changedUnit.Unit.TenantId, cancellationToken);
            events.Add(ToStatusChanged(
                changedUnit.Unit,
                new UnitStatusRoute(tenantKey, changedUnit.SiteKey, changedUnit.Unit.Key)));
        }

        await transaction.CommitAsync(cancellationToken);
        return events;
    }

    public Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken) =>
        repository.GetStaleUnitsAsync(minutes, cancellationToken);

    private static UnitStatusChanged ToStatusChanged(
        UnitDto unit,
        UnitStatusRoute route) =>
        new(
            unit.TenantId,
            unit.SiteId,
            unit.Id,
            route.TenantKey,
            route.SiteKey,
            route.UnitKey,
            unit.Name,
            unit.Status,
            DateTimeOffset.UtcNow,
            unit.LastSeenUtc);
}
