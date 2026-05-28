using Alpha.Scada.Contracts;
using Alpha.Scada.Telemetry.Contracts;
using Npgsql;

namespace Alpha.Scada.Asset.Infrastructure;

public sealed class AssetRepository(NpgsqlDataSource dataSource)
{
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
        await using var command = new NpgsqlCommand("""
            update units
            set status = 'online', last_seen_utc = now()
            where id = @unit_id
            returning id, tenant_id, site_id, key, name, model, status, last_seen_utc
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        return (await ReadUnitsAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            with changed as (
                update units
                set status = 'offline'
                where status <> 'offline'
                  and (last_seen_utc is null or last_seen_utc < now() - (@minutes::text || ' minutes')::interval)
                returning id, tenant_id, site_id, key, name, model, status, last_seen_utc
            )
            select id, tenant_id, site_id, key, name, model, status, last_seen_utc
            from changed
            """, connection);
        command.Parameters.AddWithValue("minutes", minutes);
        return await ReadUnitsAsync(command, cancellationToken);
    }

    public async Task MarkShadowSeenAsync(TelemetryBatchStored message, CancellationToken cancellationToken)
    {
        var lastSeen = message.Samples.Count == 0
            ? message.StoredAtUtc
            : message.Samples.Max(sample => sample.SourceTimestampUtc);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            insert into unit_last_seen_shadow (tenant_id, unit_id, tenant_key, site_key, unit_key, last_seen_utc, updated_at_utc)
            values (@tenant_id, @unit_id, @tenant_key, @site_key, @unit_key, @last_seen_utc, now())
            on conflict (unit_id) do update
            set last_seen_utc = greatest(unit_last_seen_shadow.last_seen_utc, excluded.last_seen_utc),
                updated_at_utc = now()
            """, connection);
        command.Parameters.AddWithValue("tenant_id", message.TenantId);
        command.Parameters.AddWithValue("unit_id", message.UnitId);
        command.Parameters.AddWithValue("tenant_key", message.TenantKey);
        command.Parameters.AddWithValue("site_key", message.SiteKey);
        command.Parameters.AddWithValue("unit_key", message.UnitKey);
        command.Parameters.AddWithValue("last_seen_utc", lastSeen);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<UnitDto>> GetStaleUnitsAsync(int minutes, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
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
}
