/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Identity/Infrastructure/IdentityMigrator.cs
- Module role: Alpha.Scada.Identity is the identity service. It owns local users, password hashing, role assignment, and JWT issuance, so other services can trust bearer-token claims instead of duplicating credential logic.
- Local role: This file owns database schema creation and seed data for its service. In raw Npgsql systems this replaces an EF migration class.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. C# records are concise immutable data carriers with value-based equality, useful for DTOs and domain events. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Security.Cryptography;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Identity.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class IdentityMigrator(
// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    NpgsqlDataSource dataSource,
// LEARN: continues an argument/object/collection initializer onto the next line.
    IConfiguration configuration,
// LEARN: continues an argument/object/collection initializer onto the next line.
    IHostEnvironment environment,
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    ILogger<IdentityMigrator> logger) : SqlDatabaseMigrator(dataSource, logger)
{
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    [
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new("001_initial", SchemaSql)
    ];

// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
    protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var seedDemoUsers = configuration.GetValue<bool?>("Seed:DemoUsers") ?? environment.IsDevelopment();
// LEARN: branches only when the boolean condition is true.
        if (!seedDemoUsers)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await EnsureBootstrapAdminAsync(connection, cancellationToken);
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: loops over each item in a collection.
        foreach (var user in SeedUsers)
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var seed = new NpgsqlCommand("""
                insert into users (id, tenant_id, email, display_name, password_hash, role)
                values (@id, @tenant_id, @email, @display_name, @password_hash, @role)
                on conflict (email) do nothing
                """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
            seed.Parameters.AddWithValue("id", user.Id);
// LEARN: executes one C# statement; semicolons terminate most statements.
            seed.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
            seed.Parameters.AddWithValue("email", user.Email);
// LEARN: executes one C# statement; semicolons terminate most statements.
            seed.Parameters.AddWithValue("display_name", user.DisplayName);
// LEARN: executes one C# statement; semicolons terminate most statements.
            seed.Parameters.AddWithValue("password_hash", PasswordHasher.Hash(user.Password));
// LEARN: executes one C# statement; semicolons terminate most statements.
            seed.Parameters.AddWithValue("role", user.Role);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await seed.ExecuteNonQueryAsync(cancellationToken);
        }

// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogInformation("Identity database is ready with development demo users.");
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    private async Task EnsureBootstrapAdminAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var usersExist = new NpgsqlCommand("select exists (select 1 from users)", connection);
// LEARN: branches only when the boolean condition is true.
        if ((bool)(await usersExist.ExecuteScalarAsync(cancellationToken) ?? false))
        {
// LEARN: returns a value or exits the current method.
            return;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var email = configuration["Seed:BootstrapAdminEmail"] ?? "bootstrap-admin@local";
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var tenantId = Guid.TryParse(configuration["Seed:BootstrapTenantId"], out var configuredTenantId)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            ? configuredTenantId
// LEARN: executes one C# statement; semicolons terminate most statements.
            : Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var password = GeneratePassword();

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var seed = new NpgsqlCommand("""
            insert into users (id, tenant_id, email, display_name, password_hash, role)
            values (gen_random_uuid(), @tenant_id, @email, @display_name, @password_hash, @role)
            on conflict (email) do nothing
            """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        seed.Parameters.AddWithValue("tenant_id", tenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        seed.Parameters.AddWithValue("email", email);
// LEARN: executes one C# statement; semicolons terminate most statements.
        seed.Parameters.AddWithValue("display_name", "Bootstrap Admin");
// LEARN: executes one C# statement; semicolons terminate most statements.
        seed.Parameters.AddWithValue("password_hash", PasswordHasher.Hash(password));
// LEARN: executes one C# statement; semicolons terminate most statements.
        seed.Parameters.AddWithValue("role", Roles.Admin);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await seed.ExecuteNonQueryAsync(cancellationToken);

// LEARN: writes structured log output; placeholders become searchable log fields.
        logger.LogWarning("Created bootstrap admin user {Email} with temporary password {Password}. Rotate this credential immediately.", email, password);
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static string GeneratePassword()
    {
// LEARN: returns a value or exits the current method.
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    }

// LEARN: declares a member with explicit visibility so the type boundary is clear.
    private static readonly SeedUser[] SeedUsers =
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    [
// LEARN: continues an argument/object/collection initializer onto the next line.
        new(Guid.Parse("40000000-0000-0000-0000-000000000001"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "admin@alpha.local", "Platform Admin", Roles.Admin, "ChangeMe!123"),
// LEARN: continues an argument/object/collection initializer onto the next line.
        new(Guid.Parse("40000000-0000-0000-0000-000000000002"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "operator@alpha.local", "Platform Operator", Roles.Operator, "ChangeMe!123"),
// LEARN: continues an argument/object/collection initializer onto the next line.
        new(Guid.Parse("40000000-0000-0000-0000-000000000003"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "viewer@alpha.local", "Platform Viewer", Roles.Viewer, "ChangeMe!123"),
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new(Guid.Parse("40000000-0000-0000-0000-000000000004"), Guid.Parse("10000000-0000-0000-0000-000000000001"), "support@alpha.local", "Platform Support", Roles.SupportEngineer, "ChangeMe!123")
    ];

// LEARN: declares a member with explicit visibility so the type boundary is clear.
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

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private sealed record SeedUser(Guid Id, Guid TenantId, string Email, string DisplayName, string Role, string Password);
}
