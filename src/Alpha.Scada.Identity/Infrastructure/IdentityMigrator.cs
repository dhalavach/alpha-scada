using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Npgsql;
using System.Security.Cryptography;

namespace Alpha.Scada.Identity.Infrastructure;

public sealed class IdentityMigrator(
    NpgsqlDataSource dataSource,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<IdentityMigrator> logger) : SqlDatabaseMigrator(dataSource, logger)
{
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
    [
        new("001_initial", SchemaSql),
        new("002_lockout", """
            alter table users
                add column if not exists failed_login_count int not null default 0,
                add column if not exists locked_until_utc timestamptz;
            """)
    ];

    protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var seedDemoData = configuration.GetValue<bool?>("Seed:DemoData") ?? environment.IsDevelopment();
        if (!seedDemoData)
        {
            await EnsureBootstrapAdminAsync(connection, cancellationToken);
            return;
        }

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

        logger.LogInformation("Identity database is ready with development demo users.");
    }

    private async Task EnsureBootstrapAdminAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var usersExist = new NpgsqlCommand("select exists (select 1 from users)", connection);
        if ((bool)(await usersExist.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            return;
        }

        var email = configuration["Seed:BootstrapAdminEmail"] ?? "bootstrap-admin@local";
        var tenantId = Guid.TryParse(configuration["Seed:BootstrapTenantId"], out var configuredTenantId)
            ? configuredTenantId
            : Guid.Parse("10000000-0000-0000-0000-000000000001");
        var password = GeneratePassword();

        await using var seed = new NpgsqlCommand("""
            insert into users (id, tenant_id, email, display_name, password_hash, role)
            values (gen_random_uuid(), @tenant_id, @email, @display_name, @password_hash, @role)
            on conflict (email) do nothing
            """, connection);
        seed.Parameters.AddWithValue("tenant_id", tenantId);
        seed.Parameters.AddWithValue("email", email);
        seed.Parameters.AddWithValue("display_name", "Bootstrap Admin");
        seed.Parameters.AddWithValue("password_hash", PasswordHasher.Hash(password));
        seed.Parameters.AddWithValue("role", Roles.Admin);
        await seed.ExecuteNonQueryAsync(cancellationToken);

        logger.LogWarning("Created bootstrap admin user {Email} with temporary password {Password}. Rotate this credential immediately.", email, password);
    }

    private static string GeneratePassword()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
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
