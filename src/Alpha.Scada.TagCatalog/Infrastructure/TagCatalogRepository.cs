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

    public async Task<ReportProfileDto?> GetReportProfileAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var profileCommand = new NpgsqlCommand("""
            select biochar_yield_m3_per_kg
            from report_profiles
            where tenant_id = @tenant_id and unit_id = @unit_id
            """, connection);
        profileCommand.Parameters.AddWithValue("tenant_id", tenantId);
        profileCommand.Parameters.AddWithValue("unit_id", unitId);

        double biocharYield;
        await using (var reader = await profileCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            biocharYield = reader.GetDouble(0);
        }

        await using var bindingsCommand = new NpgsqlCommand("""
            select b.metric_key,
                   b.tag_id,
                   d.aggregation_type,
                   coalesce(b.scale, d.default_scale) as scale,
                   coalesce(b.threshold, d.default_threshold) as threshold
            from report_metric_bindings b
            join report_metric_definitions d on d.metric_key = b.metric_key
            where b.tenant_id = @tenant_id and b.unit_id = @unit_id
            order by b.metric_key
            """, connection);
        bindingsCommand.Parameters.AddWithValue("tenant_id", tenantId);
        bindingsCommand.Parameters.AddWithValue("unit_id", unitId);

        var bindings = new List<ReportMetricBindingDto>();
        await using var bindingsReader = await bindingsCommand.ExecuteReaderAsync(cancellationToken);
        while (await bindingsReader.ReadAsync(cancellationToken))
        {
            bindings.Add(new ReportMetricBindingDto(
                bindingsReader.GetString(0),
                bindingsReader.GetGuid(1),
                bindingsReader.GetString(2),
                bindingsReader.GetDouble(3),
                bindingsReader.IsDBNull(4) ? null : bindingsReader.GetDouble(4)));
        }

        return new ReportProfileDto(
            tenantId,
            unitId,
            biocharYield,
            bindings);
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
