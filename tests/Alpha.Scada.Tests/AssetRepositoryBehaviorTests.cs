/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/AssetRepositoryBehaviorTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Asset.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Logging.Abstractions;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class AssetRepositoryBehaviorTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid OtherTenantId = Guid.Parse("10000000-0000-0000-0000-000000000002");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid OtherSiteId = Guid.Parse("20000000-0000-0000-0000-000000000002");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Asset_repository_applies_tenant_visibility_to_sites_and_units()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new AssetRepository(dataSource);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var viewerSites = await repository.GetSitesAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var supportSites = await repository.GetSitesAsync(User(TenantId, Roles.SupportEngineer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var viewerUnits = await repository.GetUnitsForSiteAsync(SiteId, User(TenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var hiddenUnits = await repository.GetUnitsForSiteAsync(OtherSiteId, User(TenantId, Roles.Viewer), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(viewerSites);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(2, supportSites.Count);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(viewerUnits);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(hiddenUnits);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Asset_repository_resolves_units_and_routes_by_keys()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new AssetRepository(dataSource);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var unit = await repository.GetUnitAsync(UnitId, User(TenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var hidden = await repository.GetUnitAsync(UnitId, User(OtherTenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var resolved = await repository.ResolveUnitAsync(TenantId, "demo-energy-site", "chp-demo-001", CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var route = await repository.GetUnitRouteAsync(UnitId, CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.NotNull(unit);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Null(hidden);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(UnitId, resolved?.UnitId);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal("demo-energy-site", route?.SiteKey);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal("chp-demo-001", route?.UnitKey);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Stale_units_can_be_detected_and_marked_offline()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new AssetRepository(dataSource);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await MakeUnitStaleAsync(connectionString, UnitId);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var stale = await repository.GetStaleUnitsAsync(2, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var changed = await repository.MarkStaleUnitsOfflineAsync(2, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var changedAgain = await repository.MarkStaleUnitsOfflineAsync(2, CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(stale);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(changed);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(changedAgain);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal("offline", changed.Single().Unit.Status);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal("demo-energy-site", changed.Single().SiteKey);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static CurrentUserDto User(Guid tenantId, string role) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task MakeUnitStaleAsync(string connectionString, Guid unitId)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            update units
            set status = 'online',
                last_seen_utc = now();

            update units
            set status = 'online',
                last_seen_utc = now() - interval '10 minutes'
            where id = @unit_id
            """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", unitId);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await command.ExecuteNonQueryAsync();
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
// LEARN: executes one C# statement; semicolons terminate most statements.
        IContainer postgres;
// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: creates a new object or record instance.
            postgres = new ContainerBuilder()
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                .WithImage(TestImages.Postgres)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                .WithEnvironment("POSTGRES_DB", "alpha_test")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                .WithEnvironment("POSTGRES_USER", "alpha")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                .WithPortBinding(5432, true)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
// LEARN: executes one C# statement; semicolons terminate most statements.
                .Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.StartAsync();
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (DockerUnavailableException ex)
        {
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw SkipException.ForSkip($"Docker is not available for asset repository integration test: {ex.Message}");
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var connectionString =
// LEARN: executes one C# statement; semicolons terminate most statements.
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await run(connectionString);
        }
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task WaitForPostgresAsync(string connectionString)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
// LEARN: starts a loop that continues while its condition remains true.
        while (true)
        {
// LEARN: starts a protected block whose exceptions can be handled below.
            try
            {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await connection.OpenAsync();
// LEARN: returns a value or exits the current method.
                return;
            }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
            catch when (DateTimeOffset.UtcNow < deadline)
            {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await Task.Delay(500);
            }
        }
    }
}
