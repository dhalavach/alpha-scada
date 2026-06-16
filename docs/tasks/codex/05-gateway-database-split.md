# Task 05 — Give the Gateway its own database

Read `README.md` in this folder first for repo context.

## Goal

The Gateway uses its own logical database (`alpha_gateway`) instead of squatting in Reporting's, restoring the database-per-service rule.

## Problem

The Gateway's connection string points at **`alpha_reporting`** in all three config sources:
- `src/Alpha.Scada.Gateway/appsettings.json` → `Database=alpha_reporting`
- `docker-compose.yml` (gateway service, ~line 12) → `Database=alpha_reporting`
- `ops/k3s/services.yaml` (gateway Deployment env) → `Database=alpha_reporting`

The Gateway needs Postgres for two things: Wolverine's durable message store (`UseAlphaMessaging` → `PersistMessagesWithPostgresql`) and the `/ready` + `/metrics` operational endpoints (`AddServiceDatabase`). Because it shares `alpha_reporting`, two distinct Wolverine applications (gateway + reporting) interleave in one `wolverine` schema: their inbox/outbox/dead-letter rows and node records mix, and each service's `/metrics` error-queue gauge (`MinimalApi.MetricsAsync` counts `wolverine.wolverine_dead_letters`) reports the **other** service's dead letters too.

## Implementation steps

1. `ops/postgres/init.sql`: add, following the existing pattern exactly:
   ```sql
   SELECT 'CREATE DATABASE alpha_gateway OWNER alpha'
   WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'alpha_gateway')\gexec
   ```
2. `src/Alpha.Scada.Gateway/appsettings.json`: `Database=alpha_gateway`.
3. `docker-compose.yml` gateway service: `Database=alpha_gateway`.
4. `ops/k3s/services.yaml` gateway Deployment: `Database=alpha_gateway`.
5. Docs: in `docs/dev-setup.md` (or the messaging runbook), add one note: *existing* dev Postgres volumes were created before `alpha_gateway` existed — `init.sql` only runs on first container init, so for an existing volume either run the `CREATE DATABASE alpha_gateway OWNER alpha` statement manually (`docker compose exec postgres psql -U alpha -d alpha_identity -c "CREATE DATABASE alpha_gateway OWNER alpha"`) or recreate the volume (`docker compose down -v`). Also note that stale gateway Wolverine rows remain in `alpha_reporting`'s `wolverine` schema and are harmless.

No C# changes. The Gateway has no migrator and no domain tables — Wolverine auto-creates its storage (`AutoBuildMessageStorageOnStartup = CreateOrUpdate`).

## Constraints

- Do not touch Reporting's configuration.
- Do not add a migrator to the Gateway.
- Database name must be `alpha_gateway` (consistent with the `alpha_*` convention).

## Verification

```bash
docker compose down -v        # clean slate so init.sql runs
docker compose up --build
# gateway healthy:
curl -s http://localhost:5202/ready          # {"status":"ready"}
# wolverine schema exists in the new DB:
docker compose exec postgres psql -U alpha -d alpha_gateway -c "\dt wolverine.*"   # envelope tables listed
# end-to-end still works: log in to the UI, watch live telemetry, run a monthly report
# from the Reports screen and see it complete (exercises gateway's Wolverine publish to ALPHA_JOBS).
dotnet test Alpha.Scada.slnx --filter "FullyQualifiedName~RealtimeTenantIsolation|FullyQualifiedName~AlarmBroadcast"
```
