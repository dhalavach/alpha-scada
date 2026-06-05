/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.TagCatalog/Infrastructure/TagCatalogRepository.cs
- Module role: Alpha.Scada.TagCatalog is the tag-catalog service. It owns tag definitions, engineering units, thresholds, subsystem grouping, and report ontology/configuration rather than scattering those constants through code.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.TagCatalog.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TagCatalogRepository(NpgsqlDataSource dataSource)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<TagDto>> GetTagsForUnitAsync(Guid unitId, CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<TagDto>> ResolveTagsAsync(ResolveTagsRequest request, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<ReportProfileDto?> GetReportProfileAsync(Guid tenantId, Guid unitId, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var profileCommand = new NpgsqlCommand("""
            select availability_no_alarms_percent, availability_with_alarms_percent, biochar_yield_m3_per_kg
            from report_profiles
            where tenant_id = @tenant_id and unit_id = @unit_id
            """, connection);
        profileCommand.Parameters.AddWithValue("tenant_id", tenantId);
        profileCommand.Parameters.AddWithValue("unit_id", unitId);

        double availabilityNoAlarms;
        double availabilityWithAlarms;
        double biocharYield;
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (var reader = await profileCommand.ExecuteReaderAsync(cancellationToken))
        {
// LEARN: branches only when the boolean condition is true.
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            availabilityNoAlarms = reader.GetDouble(0);
            availabilityWithAlarms = reader.GetDouble(1);
            biocharYield = reader.GetDouble(2);
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
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
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var bindingsReader = await bindingsCommand.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
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
            availabilityNoAlarms,
            availabilityWithAlarms,
            biocharYield,
            bindings);
    }

    private static async Task<IReadOnlyCollection<TagDto>> ReadTagsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<TagDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
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
