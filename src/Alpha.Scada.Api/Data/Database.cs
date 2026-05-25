using Npgsql;

namespace Alpha.Scada.Api.Data;

public static class Database
{
    public static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=alpha_scada;Username=alpha;Password=alpha";

        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.EnableParameterLogging(false);
            return builder.Build();
        });

        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<PlatformRepository>();
    }
}
