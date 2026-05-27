using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.TagCatalog.Infrastructure;

public sealed class TagCatalogRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyCollection<TagDto>> GetTagsForUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, unit_id, key, name, subsystem, engineering_unit, alarm_low, alarm_high
            from tags
            where unit_id = @unit_id and (@is_support or tenant_id = @tenant_id)
            order by subsystem, name
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return await ReadTagsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TagDto>> ResolveTagsAsync(ResolveTagsRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, unit_id, key, name, subsystem, engineering_unit, alarm_low, alarm_high
            from tags
            where tenant_id = @tenant_id and unit_id = @unit_id and key = any(@keys)
            """, connection);
        command.Parameters.AddWithValue("tenant_id", request.TenantId);
        command.Parameters.AddWithValue("unit_id", request.UnitId);
        command.Parameters.AddWithValue("keys", request.TagKeys.ToArray());
        return await ReadTagsAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyCollection<TagDto>> ReadTagsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<TagDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TagDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

        return results;
    }
}
