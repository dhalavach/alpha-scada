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
            select id, tenant_id, email, display_name, password_hash, role, failed_login_count, locked_until_utc
            from users
            where lower(email) = lower(@email)
            """, connection);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UserAccount(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7))
            : null;
    }

    public async Task RecordFailedLoginAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update users
            set locked_until_utc = case
                    when failed_login_count + 1 >= 5 then now() + interval '15 minutes'
                    else locked_until_utc
                end,
                failed_login_count = case
                    when failed_login_count + 1 >= 5 then 0
                    else failed_login_count + 1
                end
            where id = @user_id
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetLoginFailuresAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update users
            set failed_login_count = 0,
                locked_until_utc = null
            where id = @user_id
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdatePasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update users
            set password_hash = @password_hash
            where id = @user_id
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("password_hash", passwordHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
