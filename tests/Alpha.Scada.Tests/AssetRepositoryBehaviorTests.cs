using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class AssetRepositoryBehaviorTests(PostgresContainerFixture postgres)
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

            var changed = await repository.MarkStaleUnitsOfflineAsync(2, CancellationToken.None);
            var changedAgain = await repository.MarkStaleUnitsOfflineAsync(2, CancellationToken.None);

            Assert.Single(changed);
            Assert.Empty(changedAgain);
            Assert.Equal("offline", changed.Single().Unit.Status);
            Assert.Equal("demo-energy-site", changed.Single().SiteKey);
        });
    }

    [Fact]
    public async Task Set_unit_online_updates_last_seen_without_returning_transition_when_already_online()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AssetRepository(dataSource);
            await SetUnitStateAsync(connectionString, UnitId, "online", DateTimeOffset.UtcNow.AddMinutes(-10));
            var before = await LastSeenAsync(connectionString, UnitId);

            var changed = await repository.SetUnitOnlineAsync(UnitId, CancellationToken.None);
            var after = await LastSeenAsync(connectionString, UnitId);

            Assert.Null(changed);
            Assert.True(after > before);
        });
    }

    [Fact]
    public async Task Set_unit_online_returns_transition_when_unit_was_offline()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new AssetMigrator(dataSource, NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new AssetRepository(dataSource);
            await SetUnitStateAsync(connectionString, UnitId, "offline", DateTimeOffset.UtcNow.AddMinutes(-10));

            var changed = await repository.SetUnitOnlineAsync(UnitId, CancellationToken.None);

            Assert.NotNull(changed);
            Assert.Equal("online", changed.Status);
            Assert.Equal(UnitId, changed.Id);
        });
    }

    private static CurrentUserDto User(Guid tenantId, string role) =>
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

    private static async Task SetUnitStateAsync(string connectionString, Guid unitId, string status, DateTimeOffset lastSeenUtc)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update units
            set status = @status,
                last_seen_utc = @last_seen_utc
            where id = @unit_id
            """, connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("last_seen_utc", lastSeenUtc);
        command.Parameters.AddWithValue("unit_id", unitId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<DateTimeOffset> LastSeenAsync(string connectionString, Guid unitId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select last_seen_utc from units where id = @unit_id", connection);
        command.Parameters.AddWithValue("unit_id", unitId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? reader.GetFieldValue<DateTimeOffset>(0)
            : DateTimeOffset.MinValue;
    }

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

    private async Task WithPostgresAsync(Func<string, Task> run)
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(AssetRepositoryBehaviorTests));
        await WaitForPostgresAsync(connectionString);
        await run(connectionString);
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
