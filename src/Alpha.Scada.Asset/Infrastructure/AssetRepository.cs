/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Infrastructure/AssetRepository.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Asset.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AssetRepository
{
    private readonly NpgsqlDataSource dataSource;

    public AssetRepository(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<SiteDto>> GetSitesAsync(CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, key, name, region, status
            from sites
            where @is_support or tenant_id = @tenant_id
            order by name
            """, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        var results = new List<SiteDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SiteDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        return results;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<UnitDto>> GetUnitsForSiteAsync(Guid siteId, CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc
            from units
            where site_id = @site_id and (@is_support or tenant_id = @tenant_id)
            order by name
            """, connection);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return await ReadUnitsAsync(command, cancellationToken);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UnitDto?> GetUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc
            from units
            where id = @unit_id and (@is_support or tenant_id = @tenant_id)
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return (await ReadUnitsAsync(command, cancellationToken)).FirstOrDefault();
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<ResolvedUnitDto?> ResolveUnitAsync(Guid tenantId, string siteKey, string unitKey, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select u.tenant_id, s.id, u.id, u.name, u.status
            from units u
            join sites s on s.id = u.site_id
            where u.tenant_id = @tenant_id and s.key = @site_key and u.key = @unit_key
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("site_key", siteKey);
        command.Parameters.AddWithValue("unit_key", unitKey);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ResolvedUnitDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UnitRouteDto?> GetUnitRouteAsync(Guid unitId, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select u.tenant_id, s.id, u.id, s.key, u.key, u.name
            from units u
            join sites s on s.id = u.site_id
            where u.id = @unit_id
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UnitRouteDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UnitDto?> SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var unit = await SetUnitOnlineAsync(connection, transaction, unitId, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
        return unit;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UnitDto?> SetUnitOnlineAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        Guid unitId,
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update units
            set status = 'online', last_seen_utc = now()
            where id = @unit_id
            returning id, tenant_id, site_id, key, name, model, status, last_seen_utc
            """, connection, transaction);
        command.Parameters.AddWithValue("unit_id", unitId);
        return (await ReadUnitsAsync(command, cancellationToken)).FirstOrDefault();
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<UnitStatusChange>> MarkStaleUnitsOfflineAsync(
        int minutes,
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = await MarkStaleUnitsOfflineAsync(connection, transaction, minutes, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<UnitStatusChange>> MarkStaleUnitsOfflineAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
        int minutes,
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            with changed as (
                update units u
                set status = 'offline'
                from sites s
                where s.id = u.site_id
                  and u.status <> 'offline'
                  and (u.last_seen_utc is null or u.last_seen_utc < now() - (@minutes::text || ' minutes')::interval)
                returning u.id, u.tenant_id, u.site_id, u.key, u.name, u.model, u.status, u.last_seen_utc, s.key as site_key
            )
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc, site_key
            from changed
            """, connection, transaction);
        command.Parameters.AddWithValue("minutes", minutes);
        var changed = new List<UnitStatusChange>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
            changed.Add(new UnitStatusChange(
                new UnitDto(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)),
                reader.GetString(8)));
        }

        return changed;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc
            from units
            where last_seen_utc < now() - (@minutes::text || ' minutes')::interval
            """, connection);
        command.Parameters.AddWithValue("minutes", minutes);
        return await ReadUnitsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyCollection<UnitDto>> ReadUnitsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<UnitDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new UnitDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return results;
    }

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
    public sealed record UnitStatusChange(UnitDto Unit, string SiteKey);
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record UnitStatusRoute(string TenantKey, string SiteKey, string UnitKey);
