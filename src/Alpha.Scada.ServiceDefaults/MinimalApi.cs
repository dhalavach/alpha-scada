using Microsoft.AspNetCore.Http;
using Npgsql;
using System.Globalization;
using System.Text;

namespace Alpha.Scada.ServiceDefaults;

public static class MinimalApi
{
    public static async Task<IResult> ReadyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await Database.CanConnectAsync(dataSource, cancellationToken);
        return Results.Ok(new { status = "ready" });
    }

    public static async Task<IResult> MetricsAsync(string serviceName, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        var metricName = serviceName.Replace("-", "_");
        var serviceLabel = EscapeLabel(serviceName);
        var errorQueueDepth = await CountIfTableExistsAsync(dataSource, "wolverine.wolverine_dead_letters", cancellationToken);
        var telemetrySamples = await CountIfTableExistsAsync(dataSource, "public.telemetry_samples", cancellationToken);

        var metrics = new StringBuilder();
        metrics.AppendLine(CultureInfo.InvariantCulture, $"# HELP {metricName}_up Application availability");
        metrics.AppendLine(CultureInfo.InvariantCulture, $"# TYPE {metricName}_up gauge");
        metrics.AppendLine(CultureInfo.InvariantCulture, $"{metricName}_up 1");
        metrics.AppendLine("# HELP alpha_scada_service_up Application availability by service");
        metrics.AppendLine("# TYPE alpha_scada_service_up gauge");
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_service_up{{service=\"{serviceLabel}\"}} 1");
        metrics.AppendLine("# HELP alpha_scada_wolverine_error_queue_depth Wolverine dead-lettered envelopes by service");
        metrics.AppendLine("# TYPE alpha_scada_wolverine_error_queue_depth gauge");
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_wolverine_error_queue_depth{{service=\"{serviceLabel}\"}} {errorQueueDepth}");
        metrics.AppendLine("# HELP alpha_scada_telemetry_samples_written_total Approximate persisted telemetry samples visible to this service database");
        metrics.AppendLine("# TYPE alpha_scada_telemetry_samples_written_total gauge");
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_telemetry_samples_written_total{{service=\"{serviceLabel}\"}} {telemetrySamples}");

        return Results.Text(metrics.ToString(), "text/plain; version=0.0.4; charset=utf-8");
    }

    private static async Task<long> CountIfTableExistsAsync(NpgsqlDataSource dataSource, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var exists = new NpgsqlCommand("select to_regclass(@tableName) is not null", connection);
        exists.Parameters.AddWithValue("tableName", tableName);
        if (await exists.ExecuteScalarAsync(cancellationToken) is not true)
        {
            return 0;
        }

        var sql = tableName switch
        {
            "wolverine.wolverine_dead_letters" => "select count(*) from wolverine.wolverine_dead_letters",
            "public.telemetry_samples" => "select approximate_row_count('public.telemetry_samples'::regclass)",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "Unsupported metrics table.")
        };

        await using var count = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static string EscapeLabel(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
