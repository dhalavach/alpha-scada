using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using OpenTelemetry.Exporter;

namespace Alpha.Scada.ServiceDefaults;

public static class AlphaOperationalEndpoints
{
    public static IEndpointRouteBuilder MapAlphaOperationalEndpoints(this IEndpointRouteBuilder app, string serviceName)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = serviceName, utc = DateTimeOffset.UtcNow }));
        app.MapGet("/ready", (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            MinimalApi.ReadyAsync(dataSource, cancellationToken));
        app.MapPrometheusScrapingEndpoint("/metrics");

        return app;
    }
}
