/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.TagCatalog/Infrastructure/TagCatalogRepository.cs
- Module role: Alpha.Scada.TagCatalog is the tag-catalog service. It owns tag definitions, engineering units, thresholds, subsystem grouping, and report ontology/configuration rather than scattering those constants through code.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", unitId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", request.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", request.UnitId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("keys", request.TagKeys.ToArray());
// LEARN: returns a value or exits the current method.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        profileCommand.Parameters.AddWithValue("tenant_id", tenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        profileCommand.Parameters.AddWithValue("unit_id", unitId);

// LEARN: executes one C# statement; semicolons terminate most statements.
        double availabilityNoAlarms;
// LEARN: executes one C# statement; semicolons terminate most statements.
        double availabilityWithAlarms;
// LEARN: executes one C# statement; semicolons terminate most statements.
        double biocharYield;
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (var reader = await profileCommand.ExecuteReaderAsync(cancellationToken))
        {
// LEARN: branches only when the boolean condition is true.
            if (!await reader.ReadAsync(cancellationToken))
            {
// LEARN: returns a value or exits the current method.
                return null;
            }

// LEARN: executes one C# statement; semicolons terminate most statements.
            availabilityNoAlarms = reader.GetDouble(0);
// LEARN: executes one C# statement; semicolons terminate most statements.
            availabilityWithAlarms = reader.GetDouble(1);
// LEARN: executes one C# statement; semicolons terminate most statements.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        bindingsCommand.Parameters.AddWithValue("tenant_id", tenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        bindingsCommand.Parameters.AddWithValue("unit_id", unitId);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var bindings = new List<ReportMetricBindingDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var bindingsReader = await bindingsCommand.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await bindingsReader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            bindings.Add(new ReportMetricBindingDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
                bindingsReader.GetString(0),
// LEARN: continues an argument/object/collection initializer onto the next line.
                bindingsReader.GetGuid(1),
// LEARN: continues an argument/object/collection initializer onto the next line.
                bindingsReader.GetString(2),
// LEARN: continues an argument/object/collection initializer onto the next line.
                bindingsReader.GetDouble(3),
// LEARN: executes one C# statement; semicolons terminate most statements.
                bindingsReader.IsDBNull(4) ? null : bindingsReader.GetDouble(4)));
        }

// LEARN: returns a value or exits the current method.
        return new ReportProfileDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
            tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            availabilityNoAlarms,
// LEARN: continues an argument/object/collection initializer onto the next line.
            availabilityWithAlarms,
// LEARN: continues an argument/object/collection initializer onto the next line.
            biocharYield,
// LEARN: executes one C# statement; semicolons terminate most statements.
            bindings);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<IReadOnlyCollection<TagDto>> ReadTagsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var results = new List<TagDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            results.Add(new TagDto(
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
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
// LEARN: executes one C# statement; semicolons terminate most statements.
                reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

// LEARN: returns a value or exits the current method.
        return results;
    }
}
