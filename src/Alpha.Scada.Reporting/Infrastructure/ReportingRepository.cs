/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Infrastructure/ReportingRepository.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.Contracts;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Reporting.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ReportingRepository(NpgsqlDataSource dataSource)
{
// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<MonthlyReportDto> SaveAsync(MonthlyReportDto report, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            insert into report_runs (id, tenant_id, unit_id, period, electrical_kwh, thermal_kwh, runtime_hours,
                                     availability_percent, estimated_wood_chips_kg, estimated_biochar_m3, alarm_count, generated_at_utc)
            values (@id, @tenant_id, @unit_id, @period, @electrical_kwh, @thermal_kwh, @runtime_hours,
                    @availability_percent, @wood, @biochar, @alarm_count, @generated_at_utc)
            on conflict (unit_id, period) do update
            set electrical_kwh = excluded.electrical_kwh,
                thermal_kwh = excluded.thermal_kwh,
                runtime_hours = excluded.runtime_hours,
                availability_percent = excluded.availability_percent,
                estimated_wood_chips_kg = excluded.estimated_wood_chips_kg,
                estimated_biochar_m3 = excluded.estimated_biochar_m3,
                alarm_count = excluded.alarm_count,
                generated_at_utc = excluded.generated_at_utc
            returning id
            """, connection);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var id = report.Id == Guid.Empty ? Guid.NewGuid() : report.Id;
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("id", id);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", report.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("unit_id", report.UnitId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("period", report.Period);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("electrical_kwh", report.ElectricalKwh);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("thermal_kwh", report.ThermalKwh);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("runtime_hours", report.RuntimeHours);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("availability_percent", report.AvailabilityPercent);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("wood", report.EstimatedWoodChipsKg);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("biochar", report.EstimatedBiocharM3);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("alarm_count", report.AlarmCount);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("generated_at_utc", report.GeneratedAtUtc);
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var savedId = (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? id);
// LEARN: returns a value or exits the current method.
        return report with { Id = savedId };
    }

// LEARN: declares an asynchronous method that can await non-blocking I/O.
    public async Task<IReadOnlyCollection<MonthlyReportDto>> GetMonthlyReportsAsync(CurrentUserDto user, CancellationToken cancellationToken)
    {
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var command = new NpgsqlCommand("""
            select id, tenant_id, unit_id, period, electrical_kwh, thermal_kwh, runtime_hours,
                   availability_percent, estimated_wood_chips_kg, estimated_biochar_m3, alarm_count, generated_at_utc
            from report_runs
            where @is_support or tenant_id = @tenant_id
            order by generated_at_utc desc
            limit 50
            """, connection);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
// LEARN: executes one C# statement; semicolons terminate most statements.
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
// LEARN: declares a local variable; var lets the compiler infer the C# type from the right-hand side.
        var results = new List<MonthlyReportDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
// LEARN: creates a new object or record instance.
            results.Add(new MonthlyReportDto(
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(0),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(1),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetGuid(2),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetString(3),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetDouble(4),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetDouble(5),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetDouble(6),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetDouble(7),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetDouble(8),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetDouble(9),
// LEARN: continues an argument/object/collection initializer onto the next line.
                reader.GetInt32(10),
// LEARN: executes one C# statement; semicolons terminate most statements.
                reader.GetFieldValue<DateTimeOffset>(11)));
        }

// LEARN: returns a value or exits the current method.
        return results;
    }
}
