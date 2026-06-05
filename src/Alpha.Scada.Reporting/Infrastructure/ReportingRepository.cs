/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Infrastructure/ReportingRepository.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file is the persistence adapter. It translates application requests into SQL/Npgsql calls and should avoid leaking storage details back into domain code.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible. async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Npgsql;

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
        var id = report.Id == Guid.Empty ? Guid.NewGuid() : report.Id;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", report.TenantId);
        command.Parameters.AddWithValue("unit_id", report.UnitId);
        command.Parameters.AddWithValue("period", report.Period);
        command.Parameters.AddWithValue("electrical_kwh", report.ElectricalKwh);
        command.Parameters.AddWithValue("thermal_kwh", report.ThermalKwh);
        command.Parameters.AddWithValue("runtime_hours", report.RuntimeHours);
        command.Parameters.AddWithValue("availability_percent", report.AvailabilityPercent);
        command.Parameters.AddWithValue("wood", report.EstimatedWoodChipsKg);
        command.Parameters.AddWithValue("biochar", report.EstimatedBiocharM3);
        command.Parameters.AddWithValue("alarm_count", report.AlarmCount);
        command.Parameters.AddWithValue("generated_at_utc", report.GeneratedAtUtc);
        var savedId = (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? id);
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
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("is_support", RoleRules.IsSupport(user.Role));
        var results = new List<MonthlyReportDto>();
// LEARN: opens an async-disposable resource and guarantees it is cleaned up asynchronously.
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
// LEARN: starts a loop that continues while its condition remains true.
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MonthlyReportDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetDouble(8),
                reader.GetDouble(9),
                reader.GetInt32(10),
                reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return results;
    }
}
