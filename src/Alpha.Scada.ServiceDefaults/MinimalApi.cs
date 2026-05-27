using Alpha.Scada.Contracts;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Alpha.Scada.ServiceDefaults;

public static class MinimalApi
{
    public static IResult RequireUser(HttpContext context, Func<CurrentUserDto, IResult> next)
    {
        var user = HttpUserContext.FromHeaders(context.Request.Headers);
        return user is null ? Results.Unauthorized() : next(user);
    }

    public static async Task<IResult> ReadyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await Database.CanConnectAsync(dataSource, cancellationToken);
        return Results.Ok(new { status = "ready" });
    }

    public static IResult Metrics(string serviceName)
    {
        var metricName = serviceName.Replace("-", "_");
        return Results.Text($"""
            # HELP {metricName}_up Application availability
            # TYPE {metricName}_up gauge
            {metricName}_up 1
            """, "text/plain");
    }
}
