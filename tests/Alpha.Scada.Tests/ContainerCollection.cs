using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Xunit.Sdk;

namespace Alpha.Scada.Tests;

[CollectionDefinition(Name)]
public sealed class ContainerCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "containers";
}

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private IContainer? container;
    private Exception? startupException;

    public async Task InitializeAsync()
    {
        try
        {
            container = new ContainerBuilder()
                .WithImage(TestImages.Postgres)
                .WithEnvironment("POSTGRES_DB", "alpha_test")
                .WithEnvironment("POSTGRES_USER", "alpha")
                .WithEnvironment("POSTGRES_PASSWORD", "alpha-pass")
                .WithPortBinding(5432, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
                .Build();
            await container.StartAsync();
        }
        catch (Exception ex) when (ContainerSupport.IsDockerUnavailable(ex))
        {
            startupException = ex;
        }
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        if (startupException is not null)
        {
            throw SkipException.ForSkip($"Docker is not available for shared PostgreSQL container: {startupException.Message}");
        }

        if (container is null)
        {
            throw SkipException.ForSkip("Docker is not available for shared PostgreSQL container.");
        }

        var sanitizedPrefix = Sanitize(prefix);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var databaseName = $"{sanitizedPrefix[..Math.Min(sanitizedPrefix.Length, 39)]}_{suffix}";
        await CreateDatabaseWithRetryAsync(databaseName);

        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    private async Task CreateDatabaseWithRetryAsync(string databaseName)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(AdminConnectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"""create database "{databaseName}" """, connection);
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
            {
                return;
            }
            catch (Exception ex) when (IsStartupRace(ex) && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
        }
    }

    private static bool IsStartupRace(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.CannotConnectNow }
        || exception is TimeoutException
        || exception is NpgsqlException { InnerException: TimeoutException };

    private string AdminConnectionString
    {
        get
        {
            if (container is null)
            {
                throw new InvalidOperationException("PostgreSQL container has not started.");
            }

            return $"Host={container.Hostname};Port={container.GetMappedPublicPort(5432)};Database=alpha_test;Username=alpha;Password=alpha-pass";
        }
    }

    private static string Sanitize(string prefix)
    {
        var chars = prefix
            .Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_')
            .ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "alpha_test" : sanitized;
    }
}
