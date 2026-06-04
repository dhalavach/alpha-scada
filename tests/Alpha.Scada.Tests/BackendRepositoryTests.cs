using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Application;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.TagCatalog.Infrastructure;
using Alpha.Scada.Tenant.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

public sealed class BackendRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenantId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Tenant_repository_scopes_regular_users_and_allows_support_visibility()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new TenantMigrator(dataSource, NullLogger<TenantMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TenantRepository(dataSource);

            var viewerTenants = await repository.GetTenantsAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
            var supportTenants = await repository.GetTenantsAsync(User(TenantId, Roles.SupportEngineer), CancellationToken.None);
            var resolved = await repository.ResolveAsync("demo-operator", CancellationToken.None);
            var missing = await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.Single(viewerTenants);
            Assert.Equal(2, supportTenants.Count);
            Assert.Equal(TenantId, resolved?.Id);
            Assert.Null(missing);
        });
    }

    [Fact]
    public async Task Tag_catalog_repository_resolves_known_tags_and_applies_tenant_visibility()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new TagCatalogMigrator(dataSource, NullLogger<TagCatalogMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TagCatalogRepository(dataSource);

            var visible = await repository.GetTagsForUnitAsync(UnitId, User(TenantId, Roles.Viewer), CancellationToken.None);
            var hidden = await repository.GetTagsForUnitAsync(UnitId, User(OtherTenantId, Roles.Viewer), CancellationToken.None);
            var resolved = await repository.ResolveTagsAsync(
                new ResolveTagsRequest(TenantId, UnitId, ["engine.electrical_output_kw", "unknown.tag"]),
                CancellationToken.None);

            Assert.Equal(15, visible.Count);
            Assert.Empty(hidden);
            Assert.Single(resolved);
        });
    }

    [Fact]
    public async Task Identity_auth_service_logs_success_and_failure()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32",
                    ["Seed:DemoUsers"] = "true"
                })
                .Build();
            await new IdentityMigrator(
                dataSource,
                configuration,
                new FakeHostEnvironment(),
                NullLogger<IdentityMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new IdentityRepository(dataSource);
            var service = new AuthService(repository, new JwtTokenService(configuration));

            var success = await service.LoginAsync(new LoginRequest("ADMIN@alpha.local", "ChangeMe!123"), CancellationToken.None);
            var failure = await service.LoginAsync(new LoginRequest("admin@alpha.local", "wrong"), CancellationToken.None);

            Assert.NotNull(success);
            Assert.Null(failure);
            Assert.Equal(2, await CountRowsAsync(connectionString, "audit_events"));
        });
    }

    [Fact]
    public async Task Reporting_repository_upserts_monthly_reports_and_filters_by_tenant()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await new ReportingMigrator(dataSource, NullLogger<ReportingMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new ReportingRepository(dataSource);
            var first = Report(TenantId, UnitId, "2026-06", 10);
            var second = first with { ElectricalKwh = 20, GeneratedAtUtc = first.GeneratedAtUtc.AddMinutes(1) };

            var saved = await repository.SaveAsync(first, CancellationToken.None);
            var updated = await repository.SaveAsync(second, CancellationToken.None);
            var visible = await repository.GetMonthlyReportsAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
            var hidden = await repository.GetMonthlyReportsAsync(User(OtherTenantId, Roles.Viewer), CancellationToken.None);
            var supportVisible = await repository.GetMonthlyReportsAsync(User(OtherTenantId, Roles.SupportEngineer), CancellationToken.None);

            Assert.Equal(saved.Id, updated.Id);
            Assert.Single(visible);
            Assert.Empty(hidden);
            Assert.Single(supportVisible);
            Assert.Equal(20, visible.Single().ElectricalKwh);
        });
    }

    [Fact]
    public async Task Metrics_endpoint_reports_wolverine_depth()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var result = await MinimalApi.MetricsAsync("alpha-test-service", dataSource, CancellationToken.None);
            var context = new DefaultHttpContext();
            using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
            context.RequestServices = provider;
            context.Response.Body = new MemoryStream();
            await result.ExecuteAsync(context);
            context.Response.Body.Position = 0;
            var text = await new StreamReader(context.Response.Body).ReadToEndAsync();

            Assert.Contains("alpha_scada_service_up", text);
            Assert.Contains("alpha_scada_wolverine_error_queue_depth", text);
        });
    }

    private static CurrentUserDto User(Guid tenantId, string role) =>
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

    private static MonthlyReportDto Report(Guid tenantId, Guid unitId, string period, double electricalKwh) =>
        new(
            Guid.Empty,
            tenantId,
            unitId,
            period,
            electricalKwh,
            40,
            20,
            99,
            100,
            0.1,
            2,
            DateTimeOffset.UtcNow);

    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
        var postgres = new ContainerBuilder()
            .WithImage(TestImages.Postgres)
            .WithEnvironment("POSTGRES_DB", "alpha_test")
            .WithEnvironment("POSTGRES_USER", "alpha")
            .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();

        try
        {
            await postgres.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            await postgres.DisposeAsync();
            throw SkipException.ForSkip($"Docker is not available for backend repository integration test: {ex.Message}");
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

    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Alpha.Scada.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
