using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Alpha.Scada.ServiceDefaults;

public static class MinimalApi
{
    public static async Task<IResult> ReadyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await Database.CanConnectAsync(dataSource, cancellationToken);
        return Results.Ok(new { status = "ready" });
    }
}
