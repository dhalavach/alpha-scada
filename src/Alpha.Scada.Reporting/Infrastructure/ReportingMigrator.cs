/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Reporting/Infrastructure/ReportingMigrator.cs
- Module role: Alpha.Scada.Reporting is the reporting service. It orchestrates monthly report generation by combining report ontology, telemetry aggregates, and alarm counts.
- Local role: This file owns database schema creation and seed data for its service. In raw Npgsql systems this replaces an EF migration class.
- Architecture connection: infrastructure files are allowed to know about PostgreSQL, SQL, and external protocols because they adapt the outside world to the application model.
- .NET/C# concepts to notice: Npgsql is the low-level PostgreSQL driver; SQL is explicit here so storage shape and performance stay visible.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Alpha.Scada.ServiceDefaults;
// LEARN: imports a namespace so this file can refer to its types without fully qualified names.
using Npgsql;

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Reporting.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class ReportingMigrator(NpgsqlDataSource dataSource, ILogger<ReportingMigrator> logger) :
// LEARN: continues the declaration above, usually listing constructor parameters or base types.
    SqlDatabaseMigrator(dataSource, logger)
{
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    [
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
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
