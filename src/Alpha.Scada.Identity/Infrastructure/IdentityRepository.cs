/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Identity/Infrastructure/IdentityRepository.cs
- Module role: Alpha.Scada.Identity is the identity service. It owns local users, password hashing, role assignment, and JWT issuance, so other services can trust bearer-token claims instead of duplicating credential logic.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Identity.Domain;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Identity.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class IdentityRepository(NpgsqlDataSource dataSource)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, email, display_name, password_hash, role
            from users
            where lower(email) = lower(@email)
            """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("email", email);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: returns a value or exits the current method.
        return await reader.ReadAsync(cancellationToken)
// LEARN: creates a new object or record instance.
            ? new UserAccount(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
// LEARN: executes one C# statement; semicolons terminate most statements.
            : null;
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task WriteAuditAsync(Guid? tenantId, Guid? userId, string eventType, string message, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            insert into audit_events (id, tenant_id, user_id, event_type, message, created_at_utc)
            values (gen_random_uuid(), @tenant_id, @user_id, @event_type, @message, now())
            """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", (object?)tenantId ?? DBNull.Value);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("user_id", (object?)userId ?? DBNull.Value);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("event_type", eventType);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("message", message);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    public static UserDto ToDto(UserAccount user) => new(user.Id, user.TenantId, user.Email, user.DisplayName, user.Role);
}
