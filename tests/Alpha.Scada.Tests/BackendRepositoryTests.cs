using System.Security.Cryptography;
using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Application;
using Alpha.Scada.Identity.Infrastructure;
using Alpha.Scada.Reporting.Infrastructure;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.TagCatalog.Infrastructure;
using Alpha.Scada.Tenant.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class BackendRepositoryTests(PostgresContainerFixture postgres)
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
            await new TenantMigrator(
                dataSource,
                DemoDataConfiguration(),
                new TestHostEnvironment(),
                NullLogger<TenantMigrator>.Instance).MigrateAsync(CancellationToken.None);
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
            await new TagCatalogMigrator(
                dataSource,
                DemoDataConfiguration(),
                new TestHostEnvironment(),
                NullLogger<TagCatalogMigrator>.Instance).MigrateAsync(CancellationToken.None);
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
            var configuration = TestJwt.Configuration(("Seed:DemoData", "true"));
            await new IdentityMigrator(
                dataSource,
                configuration,
                new TestHostEnvironment(),
                NullLogger<IdentityMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new IdentityRepository(dataSource);
            var service = new AuthService(repository, new JwtTokenService(configuration), TimeProvider.System);

            var success = await service.LoginAsync(new LoginRequest("ADMIN@alpha.local", "ChangeMe!123"), CancellationToken.None);
            var failure = await service.LoginAsync(new LoginRequest("admin@alpha.local", "wrong"), CancellationToken.None);

            Assert.NotNull(success);
            Assert.Null(failure);
            Assert.Equal(2, await CountRowsAsync(connectionString, "audit_events"));
        });
    }

    [Fact]
    public async Task Identity_locks_account_after_five_failures_and_allows_login_after_expiry()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var configuration = TestJwt.Configuration(("Seed:DemoData", "true"));
            await new IdentityMigrator(
                dataSource,
                configuration,
                new TestHostEnvironment(),
                NullLogger<IdentityMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new IdentityRepository(dataSource);
            var service = new AuthService(repository, new JwtTokenService(configuration), TimeProvider.System);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                Assert.Null(await service.LoginAsync(
                    new LoginRequest("admin@alpha.local", "wrong"),
                    CancellationToken.None));
            }

            Assert.Null(await service.LoginAsync(
                new LoginRequest("admin@alpha.local", "ChangeMe!123"),
                CancellationToken.None));
            Assert.Equal(1, await CountAuditAsync(connectionString, "auth.login_locked"));

            await ExpireLockoutAsync(connectionString, "admin@alpha.local");
            Assert.NotNull(await service.LoginAsync(
                new LoginRequest("admin@alpha.local", "ChangeMe!123"),
                CancellationToken.None));
            Assert.Equal(0, await FailedLoginCountAsync(connectionString, "admin@alpha.local"));
        });
    }

    [Fact]
    public async Task Successful_login_transparently_rehashes_legacy_password()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var configuration = TestJwt.Configuration(("Seed:DemoData", "true"));
            await new IdentityMigrator(
                dataSource,
                configuration,
                new TestHostEnvironment(),
                NullLogger<IdentityMigrator>.Instance).MigrateAsync(CancellationToken.None);
            await SetPasswordHashAsync(
                connectionString,
                "admin@alpha.local",
                LegacyPasswordHash("ChangeMe!123", 100_000));
            var repository = new IdentityRepository(dataSource);
            var service = new AuthService(repository, new JwtTokenService(configuration), TimeProvider.System);

            var login = await service.LoginAsync(
                new LoginRequest("admin@alpha.local", "ChangeMe!123"),
                CancellationToken.None);
            var upgraded = await PasswordHashAsync(connectionString, "admin@alpha.local");

            Assert.NotNull(login);
            Assert.False(PasswordHasher.NeedsRehash(upgraded));
            Assert.Contains($".{PasswordHasher.Iterations}.", upgraded);
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

    private static IConfiguration DemoDataConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:DemoData"] = "true"
            })
            .Build();

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

    private async Task WithPostgresAsync(Func<string, Task> run)
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(BackendRepositoryTests));
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

    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountAuditAsync(string connectionString, string eventType)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select count(*) from audit_events where event_type = @event_type",
            connection);
        command.Parameters.AddWithValue("event_type", eventType);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExpireLockoutAsync(string connectionString, string email)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update users
            set locked_until_utc = now() - interval '1 minute'
            where lower(email) = lower(@email)
            """, connection);
        command.Parameters.AddWithValue("email", email);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> FailedLoginCountAsync(string connectionString, string email)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select failed_login_count from users where lower(email) = lower(@email)",
            connection);
        command.Parameters.AddWithValue("email", email);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task SetPasswordHashAsync(string connectionString, string email, string passwordHash)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            update users
            set password_hash = @password_hash
            where lower(email) = lower(@email)
            """, connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("password_hash", passwordHash);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> PasswordHashAsync(string connectionString, string email)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select password_hash from users where lower(email) = lower(@email)",
            connection);
        command.Parameters.AddWithValue("email", email);
        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }

    private static string LegacyPasswordHash(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        return $"pbkdf2-sha256.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

}
