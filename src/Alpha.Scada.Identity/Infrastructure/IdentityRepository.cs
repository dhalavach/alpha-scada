/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Identity/Infrastructure/IdentityRepository.cs
- Module role: Alpha.Scada.Identity is the identity service. It owns local users, password hashing, role assignment, and JWT issuance, so other services can trust bearer-token claims instead of duplicating credential logic.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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
