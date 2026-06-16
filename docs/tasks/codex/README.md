# Codex Task Pack — Burning Issues (2026-06-10)

Self-contained tasks derived from `code-review-2026-06-10.md` and the 2026-06-11 quality review. Each file is a complete brief for an autonomous coding agent: problem, exact locations, step-by-step plan, constraints, and verification. Do **one task per branch/PR**, in this order:

| # | Task | Why / status |
|---|---|---|
| 1 | [01-internal-service-auth.md](01-internal-service-auth.md) | Closes anonymous cross-tenant data endpoints (security) — **done 2026-06-10** |
| 2 | [02-comm-loss-alarm-clear.md](02-comm-loss-alarm-clear.md) | Domain correctness bug: critical alarms stick forever — **done 2026-06-10** |
| 3 | [03-durable-telemetry-dlq.md](03-durable-telemetry-dlq.md) | Dead-lettered telemetry is currently lost forever — **done 2026-06-10** |
| 4 | [04-nats-permissions-and-secrets.md](04-nats-permissions-and-secrets.md) | Broker-level tenant isolation + secrets out of git — **done 2026-06-10** |
| 5 | [05-gateway-database-split.md](05-gateway-database-split.md) | Small; untangles gateway from reporting's DB — **done 2026-06-10** |
| 6 | [06-deflake-test-suite.md](06-deflake-test-suite.md) | Makes `dotnet test` trustworthy for all of the above — **done 2026-06-10** |
| 7 | [07-consistency-sweep.md](07-consistency-sweep.md) | Style consistency and ephemeral realtime broadcast listeners — **done** |
| 8 | [08-ci-and-build-standardization.md](08-ci-and-build-standardization.md) | CI and centralized build configuration — **done (`9a30076`)** |
| 9 | [09-de-demo-the-product.md](09-de-demo-the-product.md) | Demo gating and runtime-derived availability — **done (`464e25b`)** |
| 10 | [10-dead-surface-purge.md](10-dead-surface-purge.md) | Dead-surface removal and helper consolidation — **done (`3925859`)** |
| 11 | [11-alarm-outbox-single-hop.md](11-alarm-outbox-single-hop.md) | Single durable alarm outbox hop — **done (`1129d65`)** |
| 12 | [12-container-hygiene.md](12-container-hygiene.md) | Container lifecycle and build caching — **done (`293eb19`)** |
| 13 | [13-gateway-hardening.md](13-gateway-hardening.md) | Gateway validation, role gates, and Problem Details — **done (`c88baf3`)** |
| 14 | [14-ingestion-and-asset-robustness.md](14-ingestion-and-asset-robustness.md) | Resolution caching, DLQ behavior, and lock hygiene — **done (`8de460c`)** |
| 15 | [15-history-downsampling.md](15-history-downsampling.md) | Bounded raw/aggregate history reads — **done (`1e722b5`)** |
| 16 | [16-auth-hardening.md](16-auth-hardening.md) | RS256 user tokens, service tokens, rate limits, lockout, and password hardening — **done (`f2b7bd1`)** |
| 17 | [17-frontend-robustness.md](17-frontend-robustness.md) | Error handling, role-aware UI, linting, and nginx hardening — **done (`1008230`)** |
| 18 | [18-opentelemetry-observability.md](18-opentelemetry-observability.md) | OpenTelemetry metrics/tracing and truthful dashboards — **done (`9051dee`)** |
| 19 | [19-repo-and-data-hygiene.md](19-repo-and-data-hygiene.md) | File cleanup, identity data hygiene, and migration bootstrap locking — **done 2026-06-15** |

## Completed execution order (08–19)

**08 → 10 → 09 → 12 → 11 → 13 → 14 → 15 → 16 → 17 → 18 → 19**

Dependencies worth knowing:
- **11** and **12** are easier after **08** (central package management).
- **18** must land **after 10 and 11** (it deletes/ports the metrics code those tasks touch).
- **13** adds `RoleRules.CanRunReports`; **10** deletes `CanManageConfiguration` — deliberate, not a conflict.
- **17**'s role-gated buttons pair with **13**'s backend gate; either may land first.
- **16** touches every test host's JWT config — rebase cost grows the longer it waits after other test-touching tasks.

## Shared context (read once, applies to every task)

**Repo:** Alpha SCADA — multi-tenant SCADA platform. .NET 10 minimal-API microservices in `src/` (Gateway, Identity, Tenant, Asset, TagCatalog, Telemetry, Alarm, Reporting, Edge + shared `Alpha.Scada.ServiceDefaults` and `Alpha.Scada.Contracts`), React 19/Vite SPA in `src/Alpha.Scada.Web`, single test project `tests/Alpha.Scada.Tests` (xUnit + Testcontainers), ops in `docker-compose.yml` and `ops/`.

**Messaging:** NATS JetStream via WolverineFx 5.21 (`src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessaging.cs` defines streams ALPHA_EDGE / ALPHA_DOMAIN / ALPHA_JOBS; `Topics.cs` holds all subject names). Telemetry ingestion bypasses Wolverine with a raw NATS consumer (`TelemetryAdapterIngestionWorker`).

**Auth:** Identity issues RS256 user tokens; internal services use a separate HS256 service-token issuer. Issuer, audience, algorithm, and role compatibility are validated through `AddAlphaJwtAuthentication`. Roles live in `src/Alpha.Scada.Contracts/Auth/AuthContracts.cs`.

**Persistence:** Postgres (TimescaleDB image), database-per-service, hand-rolled migrators extending `SqlDatabaseMigrator` (`ServiceDefaults/DatabaseMigrations.cs`), raw Npgsql with parameterized SQL (no EF). Follow this style — do not introduce an ORM.

**Conventions:** file-scoped namespaces, primary constructors, sealed records for DTOs/messages, raw-string-literal SQL, no new NuGet dependencies unless the task says so. The build must stay at **0 warnings**.

**Commands:**
```bash
dotnet build Alpha.Scada.slnx          # must be green, 0 warnings
dotnet test  Alpha.Scada.slnx          # integration tests need Docker
docker compose up --build              # full stack; UI at :8080, gateway at :5202
```

**Testcontainers:** Docker-backed tests share a serialized collection and prewarm guidance lives in `docs/dev-setup.md`. CI treats skipped tests as failures.
