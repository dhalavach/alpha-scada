using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class ServiceDefaultsEndpointTests(PostgresContainerFixture postgres)
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
            builder.AddAlphaObservability("alpha-test-service");
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
            Assert.Contains("alpha_scada_wolverine_error_queue_depth", metrics);
            Assert.Contains("http_server", metrics);
        });
    }

    [Fact]
    public async Task Exception_handler_returns_problem_details_without_exception_text()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseAlphaExceptionHandling();
        app.MapGet("/throws", (Func<IResult>)(() =>
            throw new InvalidOperationException("sensitive database detail")));
        await app.StartAsync();
        await using (app)
        {
            using var response = await app.GetTestClient().GetAsync("/throws");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Unexpected error", body);
            Assert.DoesNotContain("sensitive database detail", body);
        }
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
    public async Task Migration_bootstrap_serializes_concurrent_migrators_on_a_fresh_database()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var first = new ProbeMigrator(dataSource);
            var second = new SecondaryProbeMigrator(dataSource);

            await Task.WhenAll(
                first.MigrateAsync(CancellationToken.None),
                second.MigrateAsync(CancellationToken.None));

            Assert.Equal(2, await CountAsync(dataSource, "select count(*) from alpha_schema_migrations"));
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from migration_probe"));
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from secondary_migration_probe"));
        });
    }

    [Fact]
    public void Service_client_registration_uses_configured_endpoint_options()
    {
        var configuration = TestJwt.Configuration(("Services:Asset", "http://asset:8080"));
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
        return (app, tokens.IssueUserToken(user, TimeSpan.FromMinutes(5)).AccessToken);
    }

    private static HttpRequestMessage TokenRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static IConfiguration ConfigurationWithSecret() => TestJwt.Configuration();

    private static async Task<long> CountAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task WithPostgresAsync(Func<string, Task> run)
    {
        var connectionString = await postgres.CreateDatabaseAsync(nameof(ServiceDefaultsEndpointTests));
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
                await using var command = new NpgsqlCommand("select 1", connection);
                await command.ExecuteScalarAsync();
                return;
            }
            catch when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
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

    private sealed class SecondaryProbeMigrator(NpgsqlDataSource dataSource) :
        SqlDatabaseMigrator(dataSource, NullLogger<SecondaryProbeMigrator>.Instance)
    {
        protected override IReadOnlyList<SqlMigration> Migrations { get; } =
        [
            new("001_probe", """
                create table if not exists secondary_migration_probe (
                    id integer primary key
                );

                insert into secondary_migration_probe (id)
                values (1)
                on conflict (id) do nothing;
                """)
        ];
    }

}
