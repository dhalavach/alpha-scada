using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Alpha.Scada.ServiceDefaults.Messaging;

namespace Alpha.Scada.ServiceDefaults;

public static class Database
{
    public static IServiceCollection AddServiceDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<WolverineTransactionalOutbox>();
        return services;
    }

    public static async Task<bool> CanConnectAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
        return true;
    }
}
