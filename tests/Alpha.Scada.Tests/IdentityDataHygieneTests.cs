using Alpha.Scada.Contracts;
using Alpha.Scada.Identity.Application;
using Alpha.Scada.Identity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Alpha.Scada.Tests;

[Collection(ContainerCollection.Name)]
public sealed class IdentityDataHygieneTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task Identity_migration_enforces_case_insensitive_email_uniqueness()
    {
        await using var dataSource = await CreateIdentityDatabaseAsync();
        await using var command = dataSource.CreateCommand("""
            insert into users (id, tenant_id, email, display_name, password_hash, role)
            values (gen_random_uuid(), gen_random_uuid(), 'ADMIN@alpha.local', 'Duplicate', 'hash', @role)
            """);
        command.Parameters.AddWithValue("role", Roles.Viewer);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("ux_users_email_lower", error.ConstraintName);
    }

    [Fact]
    public async Task Audit_messages_are_bounded_and_retention_worker_prunes_only_expired_rows()
    {
        await using var dataSource = await CreateIdentityDatabaseAsync();
        var repository = new IdentityRepository(dataSource);
        await repository.WriteAuditAsync(
            null,
            null,
            "test.long",
            new string('x', 750),
            CancellationToken.None);
        await InsertAuditAsync(dataSource, "test.old", DateTimeOffset.UtcNow.AddDays(-181));
        await InsertAuditAsync(dataSource, "test.recent", DateTimeOffset.UtcNow.AddDays(-1));

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(repository);
                services.AddSingleton(new AuditRetentionOptions(180));
                services.AddHostedService<AuditRetentionWorker>();
            })
            .Build();
        await host.StartAsync();

        await WaitUntilAsync(
            async () => await CountAuditAsync(dataSource, "test.old") == 0,
            TimeSpan.FromSeconds(5));

        Assert.Equal(500, await AuditMessageLengthAsync(dataSource, "test.long"));
        Assert.Equal(0, await CountAuditAsync(dataSource, "test.old"));
        Assert.Equal(1, await CountAuditAsync(dataSource, "test.recent"));
        await host.StopAsync();
    }

    [Fact]
    public void Audit_retention_configuration_requires_a_positive_day_count()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Audit:RetentionDays"] = "0"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            AuditRetentionOptions.FromConfiguration(configuration));

        Assert.Contains("greater than zero", error.Message);
    }

    private async Task<NpgsqlDataSource> CreateIdentityDatabaseAsync()
    {
        var dataSource = NpgsqlDataSource.Create(
            await postgres.CreateDatabaseAsync(nameof(IdentityDataHygieneTests)));
        var configuration = TestJwt.Configuration(("Seed:DemoData", "true"));
        await new IdentityMigrator(
                dataSource,
                configuration,
                new TestHostEnvironment(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IdentityMigrator>.Instance)
            .MigrateAsync(CancellationToken.None);
        return dataSource;
    }

    private static async Task InsertAuditAsync(
        NpgsqlDataSource dataSource,
        string eventType,
        DateTimeOffset createdAtUtc)
    {
        await using var command = dataSource.CreateCommand("""
            insert into audit_events (id, event_type, message, created_at_utc)
            values (gen_random_uuid(), @event_type, @message, @created_at_utc)
            """);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("message", eventType);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAuditAsync(NpgsqlDataSource dataSource, string eventType)
    {
        await using var command = dataSource.CreateCommand(
            "select count(*) from audit_events where event_type = @event_type");
        command.Parameters.AddWithValue("event_type", eventType);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<int> AuditMessageLengthAsync(
        NpgsqlDataSource dataSource,
        string eventType)
    {
        await using var command = dataSource.CreateCommand(
            "select char_length(message) from audit_events where event_type = @event_type");
        command.Parameters.AddWithValue("event_type", eventType);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met before the timeout.");
            }

            await Task.Delay(50);
        }
    }
}
