using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Domain;
using Npgsql;

namespace Alpha.Scada.Identity.Infrastructure;

public sealed class IdentityRepository(NpgsqlDataSource dataSource)
{
    public async Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, email, display_name, password_hash, role
            from users
            where lower(email) = lower(@email)
            """, connection);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UserAccount(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

    public async Task WriteAuditAsync(Guid? tenantId, Guid? userId, string eventType, string message, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            insert into audit_events (id, tenant_id, user_id, event_type, message, created_at_utc)
            values (gen_random_uuid(), @tenant_id, @user_id, @event_type, @message, now())
            """, connection);
        command.Parameters.AddWithValue("tenant_id", (object?)tenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("user_id", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static UserDto ToDto(UserAccount user) => new(user.Id, user.TenantId, user.Email, user.DisplayName, user.Role);
}
