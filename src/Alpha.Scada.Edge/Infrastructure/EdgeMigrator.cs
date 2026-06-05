/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Edge/Infrastructure/EdgeMigrator.cs
- Module role: Alpha.Scada.Edge is the edge/simulator service. In this codebase it stands in for field-side publishers by producing raw telemetry onto NATS subjects.
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
namespace Alpha.Scada.Edge.Infrastructure;

// LEARN: declares a class; sealed means no other class can inherit from it.
public sealed class EdgeMigrator(NpgsqlDataSource dataSource, ILogger<EdgeMigrator> logger) :
// LEARN: continues the declaration above, usually listing constructor parameters or base types.
    SqlDatabaseMigrator(dataSource, logger)
{
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    protected override IReadOnlyList<SqlMigration> Migrations { get; } =
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
    [
// LEARN: continues the current C# construct; indentation shows the surrounding scope.
        new("001_initial", """
            create extension if not exists pgcrypto;

            create table if not exists edge_devices (
                id uuid primary key default gen_random_uuid(),
                tenant_id uuid not null,
                site_id uuid not null,
                unit_id uuid not null,
                key text not null,
                credential_hash text,
                last_seen_utc timestamptz,
                unique (tenant_id, key)
            );
            """)
    ];
}
