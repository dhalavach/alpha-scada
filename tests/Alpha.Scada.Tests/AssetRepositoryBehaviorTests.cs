using System.Net;
using System.Net.Http.Json;
using Alpha.Scada.Asset.Application;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
            await MigrateAsync(dataSource);
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
            await MigrateAsync(dataSource);
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
            await MigrateAsync(dataSource);
            var repository = new AssetRepository(dataSource);
            await MakeUnitStaleAsync(connectionString, UnitId);

            var changed = await MarkStaleUnitsOfflineAsync(dataSource, repository, 2);
            var changedAgain = await MarkStaleUnitsOfflineAsync(dataSource, repository, 2);

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
            await MigrateAsync(dataSource);
            var repository = new AssetRepository(dataSource);
            await SetUnitStateAsync(connectionString, UnitId, "online", DateTimeOffset.UtcNow.AddMinutes(-10));
            var before = await LastSeenAsync(connectionString, UnitId);

            var changed = await SetUnitOnlineAsync(dataSource, repository, UnitId);
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
            await MigrateAsync(dataSource);
            var repository = new AssetRepository(dataSource);
            await SetUnitStateAsync(connectionString, UnitId, "offline", DateTimeOffset.UtcNow.AddMinutes(-10));

            var changed = await SetUnitOnlineAsync(dataSource, repository, UnitId);

            Assert.NotNull(changed);
            Assert.Equal("online", changed.Status);
            Assert.Equal(UnitId, changed.Id);
        });
    }

    [Fact]
    public async Task Stale_sweep_releases_unit_locks_before_tenant_resolution()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await MigrateAsync(dataSource);
            await MakeUnitStaleAsync(connectionString, UnitId);
            var repository = new AssetRepository(dataSource);
            var resolverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResolver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var resolver = new TenantKeyResolver(
                new StaticHttpClientFactory(new DelegateHandler(async (_, cancellationToken) =>
                {
                    resolverStarted.TrySetResult();
                    await releaseResolver.Task.WaitAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new TenantDto(TenantId, "demo-operator", "Demo", "EU"))
                    };
                })),
                cache);
            var service = new AssetService(repository, resolver, dataSource);

            var offlineTask = service.MarkStaleUnitsOfflineAsync(2, CancellationToken.None);
            await resolverStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                var online = await service.SetUnitOnlineAsync(
                    UnitId,
                    new UnitStatusRoute("demo-operator", "demo-energy-site", "chp-demo-001"),
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));

                Assert.NotNull(online);
                Assert.Equal("online", online.Status);
            }
            finally
            {
                releaseResolver.TrySetResult();
            }

            var offline = await offlineTask;
            Assert.Single(offline);
            Assert.Equal("offline", offline.Single().Status);
        });
    }

    private static CurrentUserDto User(Guid tenantId, string role) =>
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

    private static Task MigrateAsync(NpgsqlDataSource dataSource)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:DemoData"] = "true"
            })
            .Build();
        return new AssetMigrator(
            dataSource,
            configuration,
            new TestHostEnvironment(),
            NullLogger<AssetMigrator>.Instance).MigrateAsync(CancellationToken.None);
    }

    private static async Task<UnitDto?> SetUnitOnlineAsync(
        NpgsqlDataSource dataSource,
        AssetRepository repository,
        Guid unitId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var unit = await repository.SetUnitOnlineAsync(connection, transaction, unitId, CancellationToken.None);
        await transaction.CommitAsync();
        return unit;
    }

    private static async Task<IReadOnlyCollection<AssetRepository.UnitStatusChange>> MarkStaleUnitsOfflineAsync(
        NpgsqlDataSource dataSource,
        AssetRepository repository,
        int minutes)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var changes = await repository.MarkStaleUnitsOfflineAsync(
            connection,
            transaction,
            minutes,
            CancellationToken.None);
        await transaction.CommitAsync();
        return changes;
    }

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

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://tenant.test") };
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
