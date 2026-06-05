/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/BackendRepositoryTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. xUnit attributes mark tests; Testcontainers spins real Postgres/NATS containers so integration tests exercise production-like dependencies. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Identity.Application;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Identity.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Reporting.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.TagCatalog.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Tenant.Infrastructure;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Builders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using DotNet.Testcontainers.Containers;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Configuration;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.DependencyInjection;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.FileProviders;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Hosting;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.Extensions.Logging.Abstractions;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Xunit.Sdk;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class BackendRepositoryTests
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid OtherTenantId = Guid.Parse("10000000-0000-0000-0000-000000000002");
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static readonly Guid UnitId = Guid.Parse("30000000-0000-0000-0000-000000000001");

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Tenant_repository_scopes_regular_users_and_allows_support_visibility()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TenantMigrator(dataSource, NullLogger<TenantMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new TenantRepository(dataSource);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var viewerTenants = await repository.GetTenantsAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var supportTenants = await repository.GetTenantsAsync(User(TenantId, Roles.SupportEngineer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var resolved = await repository.ResolveAsync("demo-operator", CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var missing = await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(viewerTenants);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(2, supportTenants.Count);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(TenantId, resolved?.Id);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Null(missing);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Tag_catalog_repository_resolves_known_tags_and_applies_tenant_visibility()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new TagCatalogMigrator(dataSource, NullLogger<TagCatalogMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new TagCatalogRepository(dataSource);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var visible = await repository.GetTagsForUnitAsync(UnitId, User(TenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var hidden = await repository.GetTagsForUnitAsync(UnitId, User(OtherTenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var resolved = await repository.ResolveTagsAsync(
// LEARN: creates a new object or record instance.
                new ResolveTagsRequest(TenantId, UnitId, ["engine.electrical_output_kw", "unknown.tag"]),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
                CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(15, visible.Count);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Empty(hidden);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Single(resolved);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Identity_auth_service_logs_success_and_failure()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var configuration = new ConfigurationBuilder()
// LEARN: creates a new object or record instance.
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
// LEARN: continues an argument/object/collection initializer onto the next line.
                    ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32",
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                    ["Seed:DemoUsers"] = "true"
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
                })
// LEARN: executes one C# statement; semicolons terminate most statements.
                .Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new IdentityMigrator(
// LEARN: continues an argument/object/collection initializer onto the next line.
                dataSource,
// LEARN: continues an argument/object/collection initializer onto the next line.
                configuration,
// LEARN: creates a new object or record instance.
                new FakeHostEnvironment(),
// LEARN: passes cancellation so shutdowns, timeouts, and aborted requests can stop work cooperatively.
                NullLogger<IdentityMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new IdentityRepository(dataSource);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var service = new AuthService(repository, new JwtTokenService(configuration));

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var success = await service.LoginAsync(new LoginRequest("ADMIN@alpha.local", "ChangeMe!123"), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var failure = await service.LoginAsync(new LoginRequest("admin@alpha.local", "wrong"), CancellationToken.None);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.NotNull(success);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Null(failure);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(2, await CountRowsAsync(connectionString, "audit_events"));
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Reporting_repository_upserts_monthly_reports_and_filters_by_tenant()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await new ReportingMigrator(dataSource, NullLogger<ReportingMigrator>.Instance).MigrateAsync(CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var repository = new ReportingRepository(dataSource);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var first = Report(TenantId, UnitId, "2026-06", 10);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var second = first with { ElectricalKwh = 20, GeneratedAtUtc = first.GeneratedAtUtc.AddMinutes(1) };

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var saved = await repository.SaveAsync(first, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var updated = await repository.SaveAsync(second, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var visible = await repository.GetMonthlyReportsAsync(User(TenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var hidden = await repository.GetMonthlyReportsAsync(User(OtherTenantId, Roles.Viewer), CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
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
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Metrics_endpoint_reports_wolverine_depth()
    {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var result = await MinimalApi.MetricsAsync("alpha-test-service", dataSource, CancellationToken.None);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var context = new DefaultHttpContext();
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
            using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
// LEARN: executes one C# statement; semicolons terminate most statements.
            context.RequestServices = provider;
// LEARN: creates a new object or record instance.
            context.Response.Body = new MemoryStream();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await result.ExecuteAsync(context);
// LEARN: executes one C# statement; semicolons terminate most statements.
            context.Response.Body.Position = 0;
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
            var text = await new StreamReader(context.Response.Body).ReadToEndAsync();

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Contains("alpha_scada_service_up", text);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Contains("alpha_scada_wolverine_error_queue_depth", text);
// LEARN: executes one C# statement; semicolons terminate most statements.
        });
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static CurrentUserDto User(Guid tenantId, string role) =>
// LEARN: executes one C# statement; semicolons terminate most statements.
        new(Guid.NewGuid(), tenantId, "user@example.test", "User", role);

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static MonthlyReportDto Report(Guid tenantId, Guid unitId, string period, double electricalKwh) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new(
// LEARN: continues an argument/object/collection initializer onto the next line.
            Guid.Empty,
// LEARN: continues an argument/object/collection initializer onto the next line.
            tenantId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            unitId,
// LEARN: continues an argument/object/collection initializer onto the next line.
            period,
// LEARN: continues an argument/object/collection initializer onto the next line.
            electricalKwh,
// LEARN: continues an argument/object/collection initializer onto the next line.
            40,
// LEARN: continues an argument/object/collection initializer onto the next line.
            20,
// LEARN: continues an argument/object/collection initializer onto the next line.
            99,
// LEARN: continues an argument/object/collection initializer onto the next line.
            100,
// LEARN: continues an argument/object/collection initializer onto the next line.
            0.1,
// LEARN: continues an argument/object/collection initializer onto the next line.
            2,
// LEARN: executes one C# statement; semicolons terminate most statements.
            DateTimeOffset.UtcNow);

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var postgres = new ContainerBuilder()
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
// LEARN: throws an exception to signal that this path cannot continue safely.
            throw SkipException.ForSkip($"Docker is not available for backend repository integration test: {ex.Message}");
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

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<int> CountRowsAsync(string connectionString, string tableName)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = new NpgsqlConnection(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await connection.OpenAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand($"select count(*) from {tableName}", connection);
// LEARN: returns a value or exits the current method.
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public string EnvironmentName { get; set; } = Environments.Development;
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public string ApplicationName { get; set; } = "Alpha.Scada.Tests";
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
// LEARN: declares a property; get/init or get-only accessors expose data while controlling mutation.
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
