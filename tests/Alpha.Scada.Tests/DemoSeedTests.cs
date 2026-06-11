using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.Asset.Infrastructure;
using Alpha.Scada.TagCatalog.Infrastructure;
using Alpha.Scada.Tenant.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class DemoSeedTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task Production_migrations_do_not_create_demo_entities()
    {
        var configuration = DisabledDemoConfiguration();
        var environment = new TestHostEnvironment(Environments.Production);

        await using var tenant = await CreateDataSourceAsync("tenant");
        await new TenantMigrator(tenant, configuration, environment, NullLogger<TenantMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);
        Assert.Equal(0, await CountAsync(tenant, "tenants"));

        await using var asset = await CreateDataSourceAsync("asset");
        await new AssetMigrator(asset, configuration, environment, NullLogger<AssetMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);
        Assert.Equal(0, await CountAsync(asset, "sites"));
        Assert.Equal(0, await CountAsync(asset, "units"));

        await using var catalog = await CreateDataSourceAsync("catalog");
        await new TagCatalogMigrator(catalog, configuration, environment, NullLogger<TagCatalogMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);
        Assert.Equal(0, await CountAsync(catalog, "tags"));
        Assert.Equal(0, await CountAsync(catalog, "report_profiles"));
        Assert.Equal(4, await CountAsync(catalog, "report_metric_definitions"));
    }

    [Fact]
    public async Task Production_identity_creates_bootstrap_admin_but_not_demo_users()
    {
        var configuration = DisabledDemoConfiguration();
        await using var identity = await CreateDataSourceAsync("identity");
        await new IdentityMigrator(
                identity,
                configuration,
                new TestHostEnvironment(Environments.Production),
                NullLogger<IdentityMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);

        Assert.Equal(1, await CountAsync(identity, "users"));
        Assert.Equal(0, await ScalarAsync(
            identity,
            "select count(*) from users where email = any(array['admin@alpha.local','operator@alpha.local','viewer@alpha.local','support@alpha.local'])"));
    }

    private async Task<NpgsqlDataSource> CreateDataSourceAsync(string suffix) =>
        NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync($"{nameof(DemoSeedTests)}_{suffix}"));

    private static IConfiguration DisabledDemoConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:DemoData"] = "false",
                ["Seed:BootstrapAdminEmail"] = "bootstrap@example.test"
            })
            .Build();

    private static Task<long> CountAsync(NpgsqlDataSource dataSource, string table) =>
        ScalarAsync(dataSource, $"select count(*) from {table}");

    private static async Task<long> ScalarAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
