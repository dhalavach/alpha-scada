using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.Tenant.Infrastructure;

public sealed class TenantRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyCollection<TenantDto>> GetTenantsAsync(CurrentUserDto user, CancellationToken cancellationToken)
    {
        const string sql = """
            select id, key, name, region
            from tenants
            where @is_support or id = @tenant_id
            order by name
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        return await ReadTenantsAsync(command, cancellationToken);
    }

    public async Task<TenantDto?> ResolveAsync(string tenantKey, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, key, name, region
            from tenants
            where key = @key
            """, connection);
        command.Parameters.AddWithValue("key", tenantKey);
        return (await ReadTenantsAsync(command, cancellationToken)).FirstOrDefault();
    }

    public async Task<TenantDto?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, key, name, region
            from tenants
            where id = @tenant_id
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        return (await ReadTenantsAsync(command, cancellationToken)).FirstOrDefault();
    }

    private static async Task<IReadOnlyCollection<TenantDto>> ReadTenantsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<TenantDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TenantDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return results;
    }
}
