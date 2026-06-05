/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/AssetRepositoryBehaviorTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class AssetRepositoryBehaviorTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenantId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherSiteId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Asset_repository_applies_tenant_visibility_to_sites_and_units()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AssetRepository(dataSource);

            var viewerSites = await repository.GetSitesAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
            var supportSites = await repository.GetSitesAsync(User(TenantId, Roles.SupportEngineer), CancellationToken.None);
            var viewerUnits = await repository.GetUnitsForSiteAsync(SiteId, User(TenantId, Roles.Viewer), CancellationToken.None);
            var hiddenUnits = await repository.GetUnitsForSiteAsync(OtherSiteId, User(TenantId, Roles.Viewer), CancellationToken.None);

            Assert.Single(viewerSites);
            Assert.Equal(2, supportSites.Count);
            Assert.Single(viewerUnits);
            Assert.Empty(hiddenUnits);
        });
    }

    [Fact]
    public async Task Asset_repository_resolves_units_and_routes_by_keys()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AssetRepository(dataSource);

            var unit = await repository.GetUnitAsync(UnitId, User(TenantId, Roles.Viewer), CancellationToken.None);
            var hidden = await repository.GetUnitAsync(UnitId, User(OtherTenantId, Roles.Viewer), CancellationToken.None);
            var resolved = await repository.ResolveUnitAsync(TenantId, "demo-energy-site", "chp-demo-001", CancellationToken.None);
            var route = await repository.GetUnitRouteAsync(UnitId, CancellationToken.None);

            Assert.NotNull(unit);
            Assert.Null(hidden);
            Assert.Equal(UnitId, resolved?.UnitId);
            Assert.Equal("demo-energy-site", route?.SiteKey);
            Assert.Equal("chp-demo-001", route?.UnitKey);
        });
    }

    [Fact]
    public async Task Stale_units_can_be_detected_and_marked_offline()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AssetRepository(dataSource);
            await MakeUnitStaleAsync(connectionString, UnitId);

            var stale = await repository.GetStaleUnitsAsync(2, CancellationToken.None);
            var changed = await repository.MarkStaleUnitsOfflineAsync(2, CancellationToken.None);
            var changedAgain = await repository.MarkStaleUnitsOfflineAsync(2, CancellationToken.None);

            Assert.Single(stale);
            Assert.Single(changed);
            Assert.Empty(changedAgain);
            Assert.Equal("offline", changed.Single().Unit.Status);
            Assert.Equal("demo-energy-site", changed.Single().SiteKey);
        });
    }

    private static CurrentUserDto User(Guid tenantId, string role) =>
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

    private static async Task MakeUnitStaleAsync(string connectionString, Guid unitId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update units
            set status = 'online',
                last_seen_utc = now();

            update units
            set status = 'online',
                last_seen_utc = now() - interval '10 minutes'
            where id = @unit_id
            """, connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
        IContainer postgres;
        try
        {
            postgres = new ContainerBuilder()
                .WithImage(TestImages.Postgres)
                .WithEnvironment("POSTGRES_DB", "alpha_test")
                .WithEnvironment("POSTGRES_USER", "alpha")
                .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
                .WithPortBinding(5432, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
                .Build();
            await postgres.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            throw SkipException.ForSkip($"Docker is not available for asset repository integration test: {ex.Message}");
        }

        await using (postgres)
        {
            var connectionString =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
            await WaitForPostgresAsync(connectionString);
            await run(connectionString);
        }
    }

    private static async Task WaitForPostgresAsync(string connectionString)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }
}
