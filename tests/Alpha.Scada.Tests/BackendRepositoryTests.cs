/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/BackendRepositoryTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

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

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class BackendRepositoryTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenantId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Tenant_repository_scopes_regular_users_and_allows_support_visibility()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TenantMigrator(dataSource, NullLogger<TenantMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TenantRepository(dataSource);

            var viewerTenants = await repository.GetTenantsAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
            var supportTenants = await repository.GetTenantsAsync(User(TenantId, Roles.SupportEngineer), CancellationToken.None);
            var resolved = await repository.ResolveAsync("demo-operator", CancellationToken.None);
            var missing = await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(viewerTenants);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(2, supportTenants.Count);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(TenantId, resolved?.Id);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Null(missing);
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Tag_catalog_repository_resolves_known_tags_and_applies_tenant_visibility()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TagCatalogMigrator(dataSource, NullLogger<TagCatalogMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new TagCatalogRepository(dataSource);

            var visible = await repository.GetTagsForUnitAsync(UnitId, User(TenantId, Roles.Viewer), CancellationToken.None);
            var hidden = await repository.GetTagsForUnitAsync(UnitId, User(OtherTenantId, Roles.Viewer), CancellationToken.None);
            var resolved = await repository.ResolveTagsAsync(
                new ResolveTagsRequest(TenantId, UnitId, ["engine.electrical_output_kw", "unknown.tag"]),
                CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(15, visible.Count);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(hidden);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(resolved);
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Identity_auth_service_logs_success_and_failure()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32",
                    ["Seed:DemoUsers"] = "true"
                })
                .Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new IdentityMigrator(
                dataSource,
                configuration,
                new FakeHostEnvironment(),
                NullLogger<IdentityMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new IdentityRepository(dataSource);
            var service = new AuthService(repository, new JwtTokenService(configuration));

            var success = await service.LoginAsync(new LoginRequest("ADMIN@alpha.local", "ChangeMe!123"), CancellationToken.None);
            var failure = await service.LoginAsync(new LoginRequest("admin@alpha.local", "wrong"), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.NotNull(success);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Null(failure);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(2, await CountRowsAsync(connectionString, "audit_events"));
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Reporting_repository_upserts_monthly_reports_and_filters_by_tenant()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new ReportingMigrator(dataSource, NullLogger<ReportingMigrator>.Instance).MigrateAsync(CancellationToken.None);
            var repository = new ReportingRepository(dataSource);
            var first = Report(TenantId, UnitId, "2026-06", 10);
            var second = first with { ElectricalKwh = 20, GeneratedAtUtc = first.GeneratedAtUtc.AddMinutes(1) };

            var saved = await repository.SaveAsync(first, CancellationToken.None);
            var updated = await repository.SaveAsync(second, CancellationToken.None);
            var visible = await repository.GetMonthlyReportsAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
            var hidden = await repository.GetMonthlyReportsAsync(User(OtherTenantId, Roles.Viewer), CancellationToken.None);
            var supportVisible = await repository.GetMonthlyReportsAsync(User(OtherTenantId, Roles.SupportEngineer), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(saved.Id, updated.Id);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(visible);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(hidden);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(supportVisible);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(20, visible.Single().ElectricalKwh);
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Metrics_endpoint_reports_wolverine_depth()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var result = await MinimalApi.MetricsAsync("alpha-test-service", dataSource, CancellationToken.None);
            var context = new DefaultHttpContext();
            using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
            context.RequestServices = provider;
            context.Response.Body = new MemoryStream();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await result.ExecuteAsync(context);
            context.Response.Body.Position = 0;
            var text = await new StreamReader(context.Response.Body).ReadToEndAsync();

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Contains("alpha_scada_service_up", text);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Contains("alpha_scada_wolverine_error_queue_depth", text);
        });
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static CurrentUserDto User(Guid tenantId, string role) =>
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
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

// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.StartAsync();
        }
// LEARN: handles a specific exception path; filters with when further narrow when it applies.
        catch (DockerUnavailableException ex)
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.DisposeAsync();
            throw SkipException.ForSkip($"Docker is not available for backend repository integration test: {ex.Message}");
        }

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using (postgres)
        {
            var connectionString =
                $"Host={postgres.Hostname};Port={postgres.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await run(connectionString);
        }
    }

    private static async Task WaitForPostgresAsync(string connectionString)
    {
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

    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Alpha.Scada.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
