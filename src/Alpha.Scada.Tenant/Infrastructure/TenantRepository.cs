/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Tenant/Infrastructure/TenantRepository.cs
- Module role: Alpha.Scada.Tenant is the tenant registry. It is the source of truth for customer/operator records and tenant keys used to scope every downstream asset, tag, telemetry, alarm, and report query.
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
namespace Alpha.Scada.Tenant.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class TenantRepository(NpgsqlDataSource dataSource)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<TenantDto>> GetTenantsAsync(CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: declares a compile-time constant; callers cannot change this value.
        const string sql = """
            select id, key, name, region
            from tenants
            where @is_support or id = @tenant_id
            order by name
            """;

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(sql, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: returns a value or exits the current method.
        return await ReadTenantsAsync(command, cancellationToken);
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<TenantDto?> ResolveAsync(string tenantKey, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, key, name, region
            from tenants
            where key = @key
            """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("key", tenantKey);
// LEARN: returns a value or exits the current method.
        return (await ReadTenantsAsync(command, cancellationToken)).FirstOrDefault();
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<TenantDto?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, key, name, region
            from tenants
            where id = @tenant_id
            """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", tenantId);
// LEARN: returns a value or exits the current method.
        return (await ReadTenantsAsync(command, cancellationToken)).FirstOrDefault();
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<IReadOnlyCollection<TenantDto>> ReadTenantsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var results = new List<TenantDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            results.Add(new TenantDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

// LEARN: returns a value or exits the current method.
        return results;
    }
}
