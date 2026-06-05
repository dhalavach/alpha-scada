/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.ServiceDefaults/MinimalApi.cs
- Module role: Alpha.Scada.ServiceDefaults is shared platform infrastructure. It centralizes auth, service clients, resiliency, operational endpoints, database setup, and Wolverine/NATS conventions so services stay small.
- Local role: This file contributes one focused piece of the service; read it together with the adjacent Domain, Application, Infrastructure, and Program files.
- Architecture connection: ServiceDefaults is deliberately shared infrastructure; keep reusable platform concerns here, not domain-specific business logic.
- .NET/C# concepts to notice: Wolverine is the in-process messaging abstraction; it routes commands/events and applies retry, inbox/outbox, and transport policies configured in ServiceDefaults. Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Microsoft.AspNetCore.Http;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Globalization;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using System.Text;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.ServiceDefaults;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class MinimalApi
{
// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static async Task<IResult> ReadyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
// LEARN: awaits asynchronous work so the current thread can be reused while I/O is pending.
        await Database.CanConnectAsync(dataSource, cancellationToken);
// LEARN: returns a value or exits the current method.
        return Results.Ok(new { status = "ready" });
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    public static async Task<IResult> MetricsAsync(string serviceName, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var metricName = serviceName.Replace("-", "_");
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var serviceLabel = EscapeLabel(serviceName);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var errorQueueDepth = await CountIfTableExistsAsync(dataSource, "wolverine.wolverine_dead_letters", cancellationToken);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var telemetrySamples = await CountIfTableExistsAsync(dataSource, "public.telemetry_samples", cancellationToken);

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var metrics = new StringBuilder();
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine(CultureInfo.InvariantCulture, $"# HELP {metricName}_up Application availability");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine(CultureInfo.InvariantCulture, $"# TYPE {metricName}_up gauge");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine(CultureInfo.InvariantCulture, $"{metricName}_up 1");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine("# HELP alpha_scada_service_up Application availability by service");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine("# TYPE alpha_scada_service_up gauge");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_service_up{{service=\"{serviceLabel}\"}} 1");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine("# HELP alpha_scada_wolverine_error_queue_depth Wolverine dead-lettered envelopes by service");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine("# TYPE alpha_scada_wolverine_error_queue_depth gauge");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_wolverine_error_queue_depth{{service=\"{serviceLabel}\"}} {errorQueueDepth}");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine("# HELP alpha_scada_telemetry_samples_written_total Approximate persisted telemetry samples visible to this service database");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine("# TYPE alpha_scada_telemetry_samples_written_total gauge");
// LEARN: executes one C# statement; semicolons terminate most statements.
        metrics.AppendLine(CultureInfo.InvariantCulture, $"alpha_scada_telemetry_samples_written_total{{service=\"{serviceLabel}\"}} {telemetrySamples}");

// LEARN: returns a value or exits the current method.
        return Results.Text(metrics.ToString(), "text/plain; version=0.0.4; charset=utf-8");
    }

// LEARN: declares a member such as a method or constructor; parameters describe what collaborators/data it needs.
    private static async Task<long> CountIfTableExistsAsync(NpgsqlDataSource dataSource, string tableName, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var exists = new NpgsqlCommand("select to_regclass(@tableName) is not null", connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        exists.Parameters.AddWithValue("tableName", tableName);
// LEARN: branches only when the boolean condition is true.
        if (await exists.ExecuteScalarAsync(cancellationToken) is not true)
        {
// LEARN: returns a value or exits the current method.
            return 0;
        }

// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var sql = tableName switch
        {
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            "wolverine.wolverine_dead_letters" => "select count(*) from wolverine.wolverine_dead_letters",
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            "public.telemetry_samples" => "select approximate_row_count('public.telemetry_samples'::regclass)",
// LEARN: uses pattern/expression syntax to map an input to an output or behavior.
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "Unsupported metrics table.")
        };

// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var count = new NpgsqlCommand(sql, connection);
// LEARN: returns a value or exits the current method.
        return Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

// LEARN: declares an expression-bodied member, a concise C# form for a one-line implementation.
    private static string EscapeLabel(string value) =>
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
            .Replace("\"", "\\\"", StringComparison.Ordinal)
// LEARN: executes one C# statement; semicolons terminate most statements.
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
