using Alpha.Scada.Asset.Contracts;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Asset.Infrastructure;

public sealed class AssetRepository
{
    private readonly NpgsqlDataSource dataSource;
    private readonly DomainOutbox outbox;

    public AssetRepository(NpgsqlDataSource dataSource, DomainOutbox outbox)
    {
        this.dataSource = dataSource;
        this.outbox = outbox;
    }

    public AssetRepository(NpgsqlDataSource dataSource)
        : this(dataSource, new DomainOutbox())
    {
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

    public async Task<UnitDto?> SetUnitOnlineAsync(
        Guid unitId,
        UnitStatusRoute? route,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update units
            set status = 'online', last_seen_utc = now()
            where id = @unit_id
            returning id, tenant_id, site_id, key, name, model, status, last_seen_utc
            """, connection, tx);
        command.Parameters.AddWithValue("unit_id", unitId);
        var unit = (await ReadUnitsAsync(command, cancellationToken)).FirstOrDefault();

        if (unit is not null && route is not null)
        {
            await EnqueueStatusChangedAsync(connection, tx, unit, route, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return unit;
    }

    public Task<UnitDto?> SetUnitOnlineAsync(Guid unitId, CancellationToken cancellationToken) =>
        SetUnitOnlineAsync(unitId, null, cancellationToken);

    public async Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(
        int minutes,
        IReadOnlyDictionary<Guid, string> tenantKeys,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
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
            """, connection, tx);
        command.Parameters.AddWithValue("minutes", minutes);
        var changed = new List<UnitWithSiteKey>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            changed.Add(new UnitWithSiteKey(
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

        await reader.DisposeAsync();

        foreach (var changedUnit in changed)
        {
            if (tenantKeys.Count == 0)
            {
                continue;
            }

            if (!tenantKeys.TryGetValue(changedUnit.Unit.TenantId, out var tenantKey))
            {
                throw new InvalidOperationException($"Tenant key {changedUnit.Unit.TenantId} could not be resolved for status event.");
            }

            await EnqueueStatusChangedAsync(
                connection,
                tx,
                changedUnit.Unit,
                new UnitStatusRoute(tenantKey, changedUnit.SiteKey, changedUnit.Unit.Key),
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return changed.Select(unit => unit.Unit).ToArray();
    }

    public Task<IReadOnlyCollection<UnitDto>> MarkStaleUnitsOfflineAsync(int minutes, CancellationToken cancellationToken) =>
        MarkStaleUnitsOfflineAsync(minutes, new Dictionary<Guid, string>(), cancellationToken);

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

    private async Task EnqueueStatusChangedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        UnitDto unit,
        UnitStatusRoute route,
        CancellationToken cancellationToken)
    {
        await outbox.EnqueueAsync(connection, tx, new UnitStatusChanged(
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

    private sealed record UnitWithSiteKey(UnitDto Unit, string SiteKey);
}

public sealed record UnitStatusRoute(string TenantKey, string SiteKey, string UnitKey);
