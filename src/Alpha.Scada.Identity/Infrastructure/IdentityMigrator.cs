using Alpha.Scada.Contracts;
using Npgsql;

namespace Alpha.Scada.Identity.Infrastructure;

public sealed class IdentityMigrator(NpgsqlDataSource dataSource, ILogger<IdentityMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var user in SeedUsers)
        {
            await using var seed = new NpgsqlCommand("""
                insert into users (id, tenant_id, email, display_name, password_hash, role)
                values (@id, @tenant_id, @email, @display_name, @password_hash, @role)
                on conflict (email) do nothing
                """, connection);
            seed.Parameters.AddWithValue("id", user.Id);
            seed.Parameters.AddWithValue("tenant_id", user.TenantId);
            seed.Parameters.AddWithValue("email", user.Email);
            seed.Parameters.AddWithValue("display_name", user.DisplayName);
            seed.Parameters.AddWithValue("password_hash", PasswordHasher.Hash(user.Password));
            seed.Parameters.AddWithValue("role", user.Role);
            await seed.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation("Identity database is ready.");
    }

    private static readonly SeedUser[] SeedUsers =
    [
        new(Guid.Parse("40000000-0000-0000-0000-000000000001"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "admin@alpha.local", "Platform Admin", Roles.Admin, "ChangeMe!123"),
        new(Guid.Parse("40000000-0000-0000-0000-000000000002"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "operator@alpha.local", "Platform Operator", Roles.Operator, "ChangeMe!123"),
        new(Guid.Parse("40000000-0000-0000-0000-000000000003"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "viewer@alpha.local", "Platform Viewer", Roles.Viewer, "ChangeMe!123"),
        new(Guid.Parse("40000000-0000-0000-0000-000000000004"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "support@alpha.local", "Platform Support", Roles.SupportEngineer, "ChangeMe!123")
    ];

    private const string SchemaSql = """
        create table if not exists users (
            id uuid primary key,
            tenant_id uuid not null,
            email text not null unique,
            display_name text not null,
            password_hash text not null,
            role text not null,
            created_at_utc timestamptz not null default now()
        );

        create table if not exists audit_events (
            id uuid primary key,
            tenant_id uuid,
            user_id uuid,
            event_type text not null,
            message text not null,
            created_at_utc timestamptz not null default now()
        );
        """;

    private sealed record SeedUser(Guid Id, Guid TenantId, string Email, string DisplayName, string Role, string Password);
}
