/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Application/AssetService.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Asset.Application;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AssetService(
// LEARN: continues an argument/object/collection initializer onto the next line.
    AssetRepository repository,
// LEARN: continues an argument/object/collection initializer onto the next line.
    TenantKeyResolver tenantKeyResolver,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource)
{
// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<SiteDto>> GetSitesAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.GetSitesAsync(user, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<UnitDto>> GetUnitsForSiteAsync(Guid siteId, CurrentUserDto user, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.GetUnitsForSiteAsync(siteId, user, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<UnitDto?> GetUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.GetUnitAsync(unitId, user, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<ResolvedUnitDto?> ResolveUnitAsync(Guid tenantId, string siteKey, string unitKey, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.ResolveUnitAsync(tenantId, siteKey, unitKey, cancellationToken);

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<UnitRouteDto?> GetUnitRouteAsync(Guid unitId, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.GetUnitRouteAsync(unitId, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UnitStatusChanged?> SetUnitOnlineAsync(Guid unitId, UnitStatusRoute? knownRoute, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var route = knownRoute;
// LEARN: branches only when the boolean condition is true.
        if (route is null)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var unitRoute = await repository.GetUnitRouteAsync(unitId, cancellationToken)
// LEARN: creates a new object or record instance.
                ?? throw new InvalidOperationException($"Unit route {unitId} could not be resolved.");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var tenantKey = await tenantKeyResolver.ResolveAsync(unitRoute.TenantId, cancellationToken);
// LEARN: creates a new object or record instance.
            route = new UnitStatusRoute(tenantKey, unitRoute.SiteKey, unitRoute.UnitKey);
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unit = await repository.SetUnitOnlineAsync(connection, transaction, unitId, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return unit is null ? null : ToStatusChanged(unit, route);
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<UnitStatusChanged?> SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        SetUnitOnlineAsync(unitId, null, cancellationToken);

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<UnitStatusChanged>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var changed = await repository.MarkStaleUnitsOfflineAsync(connection, transaction, minutes, cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var events = new List<UnitStatusChanged>();
// LEARN: loops over each item in a collection.
        foreach (var changedUnit in changed)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var tenantKey = await tenantKeyResolver.ResolveAsync(changedUnit.Unit.TenantId, cancellationToken);
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            events.Add(ToStatusChanged(
// LEARN: continues an argument/object/collection initializer onto the next line.
                changedUnit.Unit,
// LEARN: creates a new object or record instance.
                new UnitStatusRoute(tenantKey, changedUnit.SiteKey, changedUnit.Unit.Key)));
        }

// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return events;
    }

// LEARN: declares a method that returns a Task, the .NET representation of asynchronous work.
    public Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        repository.GetStaleUnitsAsync(minutes, cancellationToken);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static UnitStatusChanged ToStatusChanged(
// LEARN: continues an argument/object/collection initializer onto the next line.
        UnitDto unit,
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        UnitStatusRoute route) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new(
// LEARN: continues an argument/object/collection initializer onto the next line.
            unit.TenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unit.SiteId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unit.Id,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.TenantKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.SiteKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            route.UnitKey,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unit.Name,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unit.Status,
// LEARN: continues an argument/object/collection initializer onto the next line.
            DateTimeOffset.UtcNow,
// LEARN: executes one C# statement; semicolons terminate most statements.
            unit.LastSeenUtc);
}
