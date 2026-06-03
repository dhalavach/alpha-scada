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

public sealed class ServiceDefaultsEndpointTests
{
    [Fact]
    public async Task Protected_endpoint_rejects_missing_token_and_binds_valid_user()
    {
        var fixture = await BuildAuthAppAsync(Roles.Operator);
        await using var app = fixture.App;
        using var client = app.GetTestClient();

        var unauthorized = await client.GetAsync("/protected");
        var authorized = await client.SendAsync(TokenRequest(HttpMethod.Get, "/protected", fixture.Token));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, authorized.StatusCode);
        Assert.Equal("operator@example.test", await authorized.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Protected_endpoint_can_apply_role_permissions()
    {
        var fixture = await BuildAuthAppAsync(Roles.Viewer);
        await using var app = fixture.App;
        using var client = app.GetTestClient();

        var response = await client.SendAsync(TokenRequest(HttpMethod.Post, "/ack", fixture.Token));

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operational_endpoints_map_health_ready_and_metrics()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(dataSource);

            var app = builder.Build();
            app.MapAlphaOperationalEndpoints("alpha-test-service");
            await app.StartAsync();

            using var client = app.GetTestClient();
            var health = await client.GetAsync("/health");
            var ready = await client.GetAsync("/ready");
            var metrics = await client.GetStringAsync("/metrics");

            Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.OK, ready.StatusCode);
            Assert.Contains("alpha_scada_service_up", metrics);
            Assert.Contains("alpha_scada_wolverine_error_queue_depth", metrics);
        });
    }

    [Fact]
    public async Task Migration_runner_records_migrations_once_and_keeps_seed_idempotent()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(dataSource);
            builder.Services.AddAlphaMigrator<ProbeMigrator>();

            var app = builder.Build();
            await app.ApplyAlphaMigrationsAsync();
            await app.ApplyAlphaMigrationsAsync();

            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from migration_probe"));
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from seed_probe"));
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from alpha_schema_migrations where migrator = 'ProbeMigrator'"));
        });
    }

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

        Assert.Equal(new Uri("http://asset:8080"), client.BaseAddress);
    }

    [Fact]
    public void Service_client_registration_fails_fast_when_required_endpoint_is_missing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAlphaServiceClients(configuration, AlphaServiceClients.Asset));

        Assert.Contains("Services:Asset", error.Message);
    }

    private static async Task<(WebApplication App, string Token)> BuildAuthAppAsync(string role)
    {
        var configuration = ConfigurationWithSecret();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddAlphaJwtAuthentication(builder.Configuration);

        var app = builder.Build();
        app.UseAlphaAuthorization();
        app.MapGet("/protected", (AuthenticatedUser user) => Results.Text(user.Current.Email))
            .RequireAuthorization();
        app.MapPost("/ack", (AuthenticatedUser user) =>
                RoleRules.CanAcknowledge(user.Current.Role)
                    ? Results.NoContent()
                    : Results.StatusCode(StatusCodes.Status403Forbidden))
            .RequireAuthorization();
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

    private static IConfiguration ConfigurationWithSecret() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32"
            })
            .Build();

    private static async Task<long> CountAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task WithPostgresAsync(Func<string, Task> run)
    {
        var postgres = new ContainerBuilder()
            .WithImage("postgres:16-alpine")
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
            throw SkipException.ForSkip($"Docker is not available for service defaults endpoint test: {ex.Message}");
        }

        try
        {
            var port = postgres.GetMappedPublicPort(5432);
            await run($"Host=localhost;Port={port};Database=alpha_test;Username=alpha;Password=alpha-pass");
        }
        finally
        {
            await postgres.DisposeAsync();
        }
    }

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

        protected override async Task SeedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("""
                create table if not exists seed_probe (
                    id integer primary key
                );

                insert into seed_probe (id)
                values (1)
                on conflict (id) do nothing;
                """, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
