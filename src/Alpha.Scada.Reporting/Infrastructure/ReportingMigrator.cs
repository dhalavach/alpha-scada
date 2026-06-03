using Alpha.Scada.ServiceDefaults;
using Npgsql;

namespace Alpha.Scada.Reporting.Infrastructure;

public sealed class ReportingMigrator(NpgsqlDataSource dataSource, ILogger<ReportingMigrator> logger) :
    SqlDatabaseMigrator(dataSource, logger)
{
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
    [
        new("001_initial", """
            create table if not exists report_runs (
                id uuid primary key,
                tenant_id uuid not null,
                unit_id uuid not null,
                period text not null,
                electrical_kwh double precision not null,
                thermal_kwh double precision not null,
                runtime_hours double precision not null,
                availability_percent double precision not null,
                estimated_wood_chips_kg double precision not null,
                estimated_biochar_m3 double precision not null,
                alarm_count integer not null,
                generated_at_utc timestamptz not null,
                unique (unit_id, period)
            );
            """)
    ];
}
