/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Asset/Infrastructure/AssetRepository.cs
- Module role: Alpha.Scada.Asset is the asset service. It owns sites, units, unit lookup by route key, online/offline status, and the bridge from stored telemetry events into operational unit health.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Asset.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AssetRepository
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private readonly NpgsqlDataSource dataSource;

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public AssetRepository(NpgsqlDataSource dataSource)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var results = new List<SiteDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            results.Add(new SiteDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("site_id", siteId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", unitId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", tenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("site_key", siteKey);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_key", unitKey);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return await reader.ReadAsync(cancellationToken)
// LEARN: creates a new object or record instance.
            ? new ResolvedUnitDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4))
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", unitId);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return await reader.ReadAsync(cancellationToken)
// LEARN: creates a new object or record instance.
            ? new UnitRouteDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
// LEARN: executes one C# statement; semicolons terminate most statements.
            : null;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UnitDto?> SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var unit = await SetUnitOnlineAsync(connection, transaction, unitId, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return unit;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UnitDto?> SetUnitOnlineAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        Guid unitId,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update units
            set status = 'online', last_seen_utc = now()
            where id = @unit_id
            returning id, tenant_id, site_id, key, name, model, status, last_seen_utc
            """, connection, transaction);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", unitId);
// LEARN: returns a value or exits the current method.
        return (await ReadUnitsAsync(command, cancellationToken)).FirstOrDefault();
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<UnitStatusChange>> MarkStaleUnitsOfflineAsync(
// LEARN: continues an argument/object/collection initializer onto the next line.
        int minutes,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
        CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var changed = await MarkStaleUnitsOfflineAsync(connection, transaction, minutes, cancellationToken);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await transaction.CommitAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return changed;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<UnitStatusChange>> MarkStaleUnitsOfflineAsync(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlConnection connection,
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        NpgsqlTransaction transaction,
// LEARN: continues an argument/object/collection initializer onto the next line.
        int minutes,
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("minutes", minutes);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var changed = new List<UnitStatusChange>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            changed.Add(new UnitStatusChange(
// LEARN: creates a new object or record instance.
                new UnitDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.GetGuid(0),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.GetGuid(1),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.GetGuid(2),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.GetString(3),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.GetString(4),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.GetString(5),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.GetString(6),
// LEARN: continues an argument/object/collection initializer onto the next line.
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)),
// LEARN: executes one C# statement; semicolons terminate most statements.
                reader.GetString(8)));
        }

// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("minutes", minutes);
// LEARN: returns a value or exits the current method.
        return await ReadUnitsAsync(command, cancellationToken);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<IReadOnlyCollection<UnitDto>> ReadUnitsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var results = new List<UnitDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            results.Add(new UnitDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(0),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(1),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(2),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(3),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(4),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(5),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(6),
// LEARN: executes one C# statement; semicolons terminate most statements.
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

// LEARN: returns a value or exits the current method.
        return results;
    }

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
    public sealed record UnitStatusChange(UnitDto Unit, string SiteKey);
}

// LEARN: declares an immutable C# record, commonly used for DTOs and message contracts.
public sealed record UnitStatusRoute(string TenantKey, string SiteKey, string UnitKey);
