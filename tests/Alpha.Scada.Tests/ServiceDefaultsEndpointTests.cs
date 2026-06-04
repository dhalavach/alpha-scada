using Alpha.Scada.Contracts;
using Alpha.Scada.ServiceDefaults;
using Alpha.Scada.ServiceDefaults.Messaging;
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
using Wolverine;
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

    [Fact]
    public async Task Transactional_outbox_rolls_back_staged_envelopes_and_keeps_committed_fallback()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await using var connection = await dataSource.OpenConnectionAsync();
            await CreateOutboxTableAsync(connection);

            var outbox = new WolverineTransactionalOutbox(
                new RecordingMessageBus(),
                new ConfigurationBuilder().Build());

            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await outbox.StoreAsync(connection, transaction, new TestEvent(), CancellationToken.None);
                await transaction.RollbackAsync();
            }

            Assert.Equal(0, await CountAsync(dataSource, "select count(*) from wolverine.wolverine_outgoing_envelopes"));

            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await outbox.StoreAsync(connection, transaction, new TestEvent(), CancellationToken.None);
                await transaction.CommitAsync();
            }

            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from wolverine.wolverine_outgoing_envelopes"));
        });
    }

    [Fact]
    public async Task Transactional_outbox_clears_fallback_after_successful_publish()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await using var connection = await dataSource.OpenConnectionAsync();
            await CreateOutboxTableAsync(connection);
            var bus = new RecordingMessageBus();
            var outbox = new WolverineTransactionalOutbox(
                bus,
                new ConfigurationBuilder().Build(),
                dataSource);

            WolverineOutboxBatch batch;
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                batch = await outbox.StoreAsync(connection, transaction, new TestEvent(), CancellationToken.None);
                await transaction.CommitAsync();
            }

            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from wolverine.wolverine_outgoing_envelopes"));

            await outbox.PublishAndClearAsync(batch, CancellationToken.None);

            Assert.Equal(1, bus.PublishCount);
            Assert.Equal(typeof(TestEvent), bus.LastPublishedMessage?.GetType());
            Assert.Equal(0, await CountAsync(dataSource, "select count(*) from wolverine.wolverine_outgoing_envelopes"));
        });
    }

    [Fact]
    public async Task Transactional_outbox_keeps_fallback_when_post_commit_publish_fails()
    {
        await WithPostgresAsync(async connectionString =>
        {
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await using var connection = await dataSource.OpenConnectionAsync();
            await CreateOutboxTableAsync(connection);
            var bus = new RecordingMessageBus { ThrowOnPublish = true };
            var outbox = new WolverineTransactionalOutbox(
                bus,
                new ConfigurationBuilder().Build(),
                dataSource);

            WolverineOutboxBatch batch;
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                batch = await outbox.StoreAsync(connection, transaction, new TestEvent(), CancellationToken.None);
                await transaction.CommitAsync();
            }

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                outbox.PublishAndClearAsync(batch, CancellationToken.None));

            Assert.Contains("Simulated publish failure", error.Message);
            Assert.Equal(1, bus.PublishCount);
            Assert.Equal(1, await CountAsync(dataSource, "select count(*) from wolverine.wolverine_outgoing_envelopes"));
        });
    }

    [Fact]
    public void Transactional_outbox_rejects_invalid_wolverine_schema_config()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wolverine:StorageSchema"] = "wolverine;drop schema public"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new WolverineTransactionalOutbox(new RecordingMessageBus(), configuration));

        Assert.Contains("Invalid Wolverine storage schema", error.Message);
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

    private static async Task CreateOutboxTableAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            create schema if not exists wolverine;

            create table if not exists wolverine.wolverine_outgoing_envelopes (
                id uuid primary key,
                owner_id integer not null,
                destination varchar not null,
                deliver_by timestamp with time zone null,
                body bytea not null,
                attempts integer null default 0,
                message_type varchar not null
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
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
            var connectionString = $"Host=localhost;Port={port};Database=alpha_test;Username=alpha;Password=alpha-pass";
            await WaitForPostgresAsync(connectionString);
            await run(connectionString);
        }
        finally
        {
            await postgres.DisposeAsync();
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

    private sealed record TestEvent;

    private sealed class RecordingMessageBus : Wolverine.IMessageBus
    {
        public string? TenantId { get; set; } = string.Empty;
        public bool ThrowOnPublish { get; init; }
        public int PublishCount { get; private set; }
        public object? LastPublishedMessage { get; private set; }

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(Uri uri) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            PreviewSubscriptions(message, new DeliveryOptions());

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        [
            new(message)
            {
                Id = Guid.NewGuid(),
                OwnerId = 0,
                Destination = new Uri("nats://alpha.test/events"),
                MessageType = "test.event",
                Data = [1, 2, 3],
                ContentType = "application/json"
            }
        ];

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            PublishCount++;
            LastPublishedMessage = message;
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("Simulated publish failure.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null) =>
            throw new NotSupportedException();
    }
}
