/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/ServiceDefaultsEndpointTests.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: Minimal APIs use route lambdas instead of controller classes; services are supplied through dependency injection parameters. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. Authentication/authorization are standard ASP.NET Core concepts: middleware validates tokens, then policies/claims decide access. IHttpClientFactory centralizes outbound HTTP clients so service-to-service calls get shared base addresses and resilience policy.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ServiceDefaultsEndpointTests
{
// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Protected_endpoint_rejects_missing_token_and_binds_valid_user()
    {
        var fixture = await BuildAuthAppAsync(Roles.Operator);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var app = fixture.App;
        using var client = app.GetTestClient();

        var unauthorized = await client.GetAsync("/protected");
        var authorized = await client.SendAsync(TokenRequest(HttpMethod.Get, "/protected", fixture.Token));

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthorized.StatusCode);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(System.Net.HttpStatusCode.OK, authorized.StatusCode);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal("operator@example.test", await authorized.Content.ReadAsStringAsync());
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Protected_endpoint_can_apply_role_permissions()
    {
        var fixture = await BuildAuthAppAsync(Roles.Viewer);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var app = fixture.App;
        using var client = app.GetTestClient();

        var response = await client.SendAsync(TokenRequest(HttpMethod.Post, "/ack", fixture.Token));

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Operational_endpoints_map_health_ready_and_metrics()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
            builder.Services.AddSingleton(dataSource);

            var app = builder.Build();
            app.MapAlphaOperationalEndpoints("alpha-test-service");
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.StartAsync();

            using var client = app.GetTestClient();
            var health = await client.GetAsync("/health");
            var ready = await client.GetAsync("/ready");
            var metrics = await client.GetStringAsync("/metrics");

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(System.Net.HttpStatusCode.OK, ready.StatusCode);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Contains("alpha_scada_service_up", metrics);
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Contains("alpha_scada_wolverine_error_queue_depth", metrics);
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task Migration_runner_records_migrations_once_and_keeps_seed_idempotent()
    {
        await WithPostgresAsync(async connectionString =>
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var builder = WebApplication.CreateBuilder();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
            builder.Services.AddLogging();
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
            builder.Services.AddSingleton(dataSource);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
            builder.Services.AddAlphaMigrator<ProbeMigrator>();

            var app = builder.Build();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.ApplyAlphaMigrationsAsync();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await app.ApplyAlphaMigrationsAsync();

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from migration_probe"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from seed_probe"));
// LEARN: asserts expected test behavior; if this condition fails, the test fails.
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from alpha_schema_migrations where migrator = 'ProbeMigrator'"));
        });
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
    public void Service_client_registration_uses_configured_endpoint_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:Asset"] = "http://asset:8080"
            })
            .Build();
        using var provider = new ServiceCollection()
            .AddAlphaServiceClients(configuration, AlphaServiceClients.Asset)
            .BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AlphaServiceClients.Asset);

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Equal(new Uri("http://asset:8080"), client.BaseAddress);
    }

// LEARN: marks this method as a single xUnit test case.
    [Fact]
    public void Service_client_registration_fails_fast_when_required_endpoint_is_missing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAlphaServiceClients(configuration, AlphaServiceClients.Asset));

// LEARN: asserts expected test behavior; if this condition fails, the test fails.
        Assert.Contains("Services:Asset", error.Message);
    }

    private static async Task<(WebApplication App, string Token)> BuildAuthAppAsync(string role)
    {
        var configuration = ConfigurationWithSecret();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
// LEARN: registers a dependency with the built-in .NET dependency-injection container.
        builder.Services.AddAlphaJwtAuthentication(builder.Configuration);

        var app = builder.Build();
        app.UseAlphaAuthorization();
// LEARN: registers an HTTP GET endpoint in ASP.NET Core Minimal APIs.
        app.MapGet("/protected", (AuthenticatedUser user) => Results.Text(user.Current.Email))
// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
            .RequireAuthorization();
// LEARN: registers an HTTP POST endpoint in ASP.NET Core Minimal APIs.
        app.MapPost("/ack", (AuthenticatedUser user) =>
                RoleRules.CanAcknowledge(user.Current.Role)
                    ? Results.NoContent()
                    : Results.StatusCode(StatusCodes.Status403Forbidden))
// LEARN: attaches ASP.NET Core authorization so callers need an accepted authenticated user/policy.
            .RequireAuthorization();
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await app.StartAsync();

        var tokens = new JwtTokenService(configuration);
        var user = new UserDto(Guid.NewGuid(), Guid.NewGuid(), "operator@example.test", "Operator", role);
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ServiceDefaultsEndpointTests>()
            .LogDebug("Issued test token for {Role}.", role);
        return (app, tokens.Issue(user, TimeSpan.FromMinutes(5)).AccessToken);
    }

    private static HttpRequestMessage TokenRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static IConfiguration ConfigurationWithSecret() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();

    private static async Task<long> CountAsync(NpgsqlDataSource dataSource, string sql)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

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
            throw SkipException.ForSkip($"Docker is not available for service defaults endpoint test: {ex.Message}");
        }

// LEARN: starts a protected block whose exceptions can be handled below.
        try
        {
            var port = postgres.GetMappedPublicPort(5432);
            var connectionString = $"Host=localhost;Port={port};Database=alpha_test;Username=alpha;Password=alpha-pass";
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await WaitForPostgresAsync(connectionString);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await run(connectionString);
        }
// LEARN: runs cleanup code whether or not the try block failed.
        finally
        {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await postgres.DisposeAsync();
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
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
                await using var command = new NpgsqlCommand("select 1", connection);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
                await command.ExecuteScalarAsync();
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

// LEARN: declares a class; sealed means no other class can inherit from it.
    private sealed class ProbeMigrator(NpgsqlDataSource dataSource) :
        SqlDatabaseMigrator(dataSource, NullLogger<ProbeMigrator>.Instance)
    {
        protected override IReadOnlyList<SqlMigration> Migrations { get; } =
        [
            new("001_probe", """
                create table if not exists migration_probe (
                    id integer primary key
                );

                insert into migration_probe (id)
                values (1)
                on conflict (id) do nothing;
                """)
        ];

// LEARN: works directly with PostgreSQL through Npgsql rather than an ORM.
        protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
            await using var command = new NpgsqlCommand("""
                create table if not exists seed_probe (
                    id integer primary key
                );

                insert into seed_probe (id)
                values (1)
                on conflict (id) do nothing;
                """, connection);
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

}
