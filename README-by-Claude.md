# Alpha SCADA Platform — README (by Claude)

> _Authored by Claude (Opus 4.8) on 2026-06-05 from a full review of the repository at commit `fc21040`._
> _This is a Claude-authored companion to the canonical [`README.md`](README.md). For the deep design, see [`docs/ARCHITECTURE-by-Claude.md`](docs/ARCHITECTURE-by-Claude.md); for diagrams, see the simplified [`docs/architecture-diagram-by-Claude-simple.md`](docs/architecture-diagram-by-Claude-simple.md) and detailed [`docs/architecture-diagram-by-Claude.md`](docs/architecture-diagram-by-Claude.md) versions._

---

## What it is

Alpha SCADA is a **multi-tenant remote-monitoring / SCADA platform for distributed industrial energy assets**, built as a .NET 10 microservice system with a React 19 UI. The reference asset is a **Combined Heat & Power (CHP) / biomass-gasification unit**, but the model is generic — **tenant → site → unit → tag** — and adapts to other small-industrial monitoring use cases.

It ingests high-frequency telemetry from edge devices, stores it as time-series history (TimescaleDB), evaluates alarms, streams live updates to the browser (SignalR), and generates monthly energy/availability reports — all decoupled over NATS JetStream.

## Architecture at a glance

| Layer | Choice |
| --- | --- |
| Frontend | React 19 + Vite, served by nginx |
| Public boundary | `Gateway` (BFF): REST `/api/*` + SignalR realtime hub |
| Domain services | Identity, Tenant, Asset, TagCatalog, Telemetry, Alarm, Reporting, Edge |
| Messaging | **NATS + JetStream** transport, **Wolverine** in-process layer (durable inbox/outbox) |
| Ingestion | Anti-corruption **adapter** boundary → one **canonical telemetry** contract |
| Historian | **TimescaleDB** (hypertable + compression + retention + continuous aggregate) |
| Data | **Database-per-service** (8 logical DBs on one PostgreSQL/TimescaleDB locally) |
| Observability | `/health`, `/ready`, `/metrics` per service; Prometheus + Grafana (`ops` profile) |
| Deploy | Docker Compose (local), k3s manifests (`ops/k3s`) |

## Core runtime flow

1. **Edge** devices (or the dev simulator) publish **raw JSON** telemetry to NATS subjects like `alpha.demo-operator.demo-energy-site.chp-demo-001.telemetry` — no platform-internal knowledge required.
2. **Telemetry** consumes the raw stream through a **protocol adapter**, normalizes to a canonical contract, resolves tenant/site/unit/tag metadata, writes current + history rows (idempotently), and publishes `TelemetryBatchStored`.
3. **Asset** updates unit online/last-seen state; **Alarm** evaluates thresholds/quality and raises or clears alarms (with **guaranteed** event delivery via a transactional outbox).
4. **Gateway** consumes telemetry/status/alarm/report events and pushes per-tenant updates to browsers over SignalR.
5. **Reporting** runs as an async NATS job: Gateway publishes `ReportRequested`; Reporting combines the tag-catalog profile, a TimescaleDB continuous-aggregate, and an alarm count, persists the report, and publishes `ReportCompleted`.

See the [ingestion sequence](docs/architecture-diagram-by-Claude.md#3-telemetry-ingestion-anti-corruption-boundary--sequence) and [messaging topology](docs/architecture-diagram-by-Claude.md#2-messaging-topology-who-publishes--who-listens) diagrams.

## Quickstart

```bash
# 1. generate local secrets (.env)
ops/scripts/dev-setup.sh

# 2. start the full local stack
docker compose up --build

# 3. open the UI
open http://localhost:8080        # Gateway API is also exposed at http://localhost:5202
```

Demo credentials are in [`docs/dev-setup.md`](docs/dev-setup.md). Demo-user seeding is enabled for Compose only (`Seed__DemoUsers=true`); do **not** enable it in production-like runs.

### Build & test

```bash
dotnet build Alpha.Scada.slnx
dotnet test  Alpha.Scada.slnx          # xUnit + Testcontainers (NATS + Postgres)

cd src/Alpha.Scada.Web && npm install && npm run build   # frontend
```

### Optional observability

```bash
docker compose --profile ops up --build   # Grafana → http://localhost:3000
```

## Service ports (local)

| Component | Host port | Notes |
| --- | --- | --- |
| Frontend (nginx) | `8080` | browser UI |
| Gateway | `5202` | REST + SignalR (`/hubs/telemetry`) |
| NATS | `4222`, `1883`, `8222` | client, MQTT (reserved), monitoring |
| PostgreSQL/TimescaleDB | `5432` | one container, 8 logical DBs |
| Grafana | `3000` | `ops` profile only |

All domain services listen on `8080` on the internal Docker network and are **not** published to the host.

## Repository map

| Path | Purpose |
| --- | --- |
| `src/Alpha.Scada.Web` | React/Vite frontend + nginx config |
| `src/Alpha.Scada.Gateway` | public API/BFF + SignalR hub |
| `src/Alpha.Scada.Identity` | users, roles, PBKDF2 hashing, JWT issuing |
| `src/Alpha.Scada.Tenant` | tenant records + key resolution |
| `src/Alpha.Scada.Asset` | sites, units, unit status, stale-unit detection |
| `src/Alpha.Scada.TagCatalog` | tag definitions, thresholds, report profiles |
| `src/Alpha.Scada.Telemetry` | ingestion adapter + TimescaleDB historian + current/history/aggregates |
| `src/Alpha.Scada.Alarm` | alarm evaluation + lifecycle + transactional outbox |
| `src/Alpha.Scada.Reporting` | monthly report generation + persistence |
| `src/Alpha.Scada.Edge` | dev CHP simulator / edge publishing boundary |
| `src/Alpha.Scada.ServiceDefaults` | shared auth, HTTP clients, DB, migrations, metrics, NATS/Wolverine conventions |
| `src/*.Contracts` | shared DTOs + domain-event records |
| `ops` | Compose/k3s assets, NATS/Postgres config, Prometheus/Grafana, backup scripts |
| `tests/Alpha.Scada.Tests` | unit + integration tests (Testcontainers) |

## Documentation

- **Architecture (deep, by Claude):** [`docs/ARCHITECTURE-by-Claude.md`](docs/ARCHITECTURE-by-Claude.md)
- **Simplified diagram (by Claude):** [`docs/architecture-diagram-by-Claude-simple.md`](docs/architecture-diagram-by-Claude-simple.md)
- **Detailed diagrams (by Claude):** [`docs/architecture-diagram-by-Claude.md`](docs/architecture-diagram-by-Claude.md)
- Canonical system overview: [`docs/system-overview.md`](docs/system-overview.md)
- Messaging ADR: [`docs/architecture-decisions/002-messaging.md`](docs/architecture-decisions/002-messaging.md)
- Messaging runbook: [`docs/messaging-runbook.md`](docs/messaging-runbook.md)
- Developer setup: [`docs/dev-setup.md`](docs/dev-setup.md)
- Sparkplug B integration task: [`docs/tasks/sparkplug-b-integration.md`](docs/tasks/sparkplug-b-integration.md)

## Design highlights

- **Anti-corruption ingestion** — devices speak their own format; a pluggable `ITelemetryAdapter` normalizes to a canonical contract. Adding a protocol = one new adapter, no pipeline change.
- **At-least-once + idempotent** telemetry (JetStream dedup + `on conflict` + ack-after-process), with **transactional-outbox guarantees** specifically for alarm events.
- **TimescaleDB as a PostgreSQL extension** — keeps the raw-Npgsql/set-based stack while adding hypertables, compression, retention, and continuous aggregates.
- **One broker** (NATS) for both edge ingress and internal events; subjects double as a tenant/site/unit unified namespace.

## Current limitations (read before relying on it)

This is an actively evolving codebase. Known gaps (full list and fixes in [`docs/ARCHITECTURE-by-Claude.md` §12](docs/ARCHITECTURE-by-Claude.md#12-known-limitations-risks--roadmap)):

- **Dead-letter durability** — the ingestion DLQ subject (`alpha._dlq.*`) is not backed by a JetStream stream, so dead-lettered payloads are not retained.
- **Service-to-service auth** — internal resolve/aggregate endpoints are unauthenticated (network-trust only); `report-aggregate` is not tenant-scoped. Intended to be hardened per client.
- **Unknown tags are dropped** — telemetry for tags not yet in the catalog is silently discarded; auto-provision/quarantine is the next planned task.
- **Sparkplug B** not implemented (the `spBv1.0.>` subject is reserved).
- **Not production-HA** — single PostgreSQL container locally; migrators run at startup; no distributed tracing yet.

## Notes on this file

This README and its sibling architecture/diagram files were generated by Claude as an independent, detailed review artifact. They reflect the repository state at commit `fc21040` (2026-06-05) and intentionally include an honest "current limitations" section. Treat the canonical `README.md` as the project's primary entry point; use these as the deeper, reviewer's-eye companion.
