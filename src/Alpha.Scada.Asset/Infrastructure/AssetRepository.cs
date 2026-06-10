using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Asset.Infrastructure;

public sealed class AssetRepository
{
    private readonly NpgsqlDataSource dataSource;

    public AssetRepository(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public async Task<IReadOnlyCollection<SiteDto>> GetSitesAsync(CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, key, name, region, status
            from sites
            where @is_support or tenant_id = @tenant_id
            order by name
            """, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        var results = new List<SiteDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SiteDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<UnitDto>> GetUnitsForSiteAsync(Guid siteId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
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

    public async Task<UnitDto?> GetUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
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

    public async Task<ResolvedUnitDto?> ResolveUnitAsync(Guid tenantId, string siteKey, string unitKey, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select u.tenant_id, s.id, u.id, u.name, u.status
            from units u
            join sites s on s.id = u.site_id
            where u.tenant_id = @tenant_id and s.key = @site_key and u.key = @unit_key
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("site_key", siteKey);
        command.Parameters.AddWithValue("unit_key", unitKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ResolvedUnitDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    public async Task<UnitRouteDto?> GetUnitRouteAsync(Guid unitId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select u.tenant_id, s.id, u.id, s.key, u.key, u.name
            from units u
            join sites s on s.id = u.site_id
            where u.id = @unit_id
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UnitRouteDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

    public async Task<UnitDto?> SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var unit = await SetUnitOnlineAsync(connection, transaction, unitId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return unit;
    }

    public async Task<UnitDto?> SetUnitOnlineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            with current_unit as (
                select id, status
                from units
                where id = @unit_id
                for update
            ),
            updated as (
                update units u
                set status = 'online',
                    last_seen_utc = now()
                from current_unit c
                where u.id = c.id
                returning u.id, u.tenant_id, u.site_id, u.key, u.name, u.model, u.status, u.last_seen_utc, c.status as previous_status
            )
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc
            from updated
            where previous_status <> 'online'
            """, connection, transaction);
        command.Parameters.AddWithValue("unit_id", unitId);
        return (await ReadUnitsAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyCollection<UnitStatusChange>> MarkStaleUnitsOfflineAsync(
        int minutes,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = await MarkStaleUnitsOfflineAsync(connection, transaction, minutes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    public async Task<IReadOnlyCollection<UnitStatusChange>> MarkStaleUnitsOfflineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int minutes,
        CancellationToken cancellationToken)
    {
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
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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

    private static async Task<IReadOnlyCollection<UnitDto>> ReadUnitsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<UnitDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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

    public sealed record UnitStatusChange(UnitDto Unit, string SiteKey);
}

public sealed record UnitStatusRoute(string TenantKey, string SiteKey, string UnitKey);
