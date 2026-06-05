# Alpha SCADA Platform

Alpha SCADA is a lightweight open-source SCADA platform for small industrial energy sites. The current implementation is a browser-based operator UI plus a .NET 10 backend, NATS JetStream messaging, TimescaleDB-backed telemetry history, alarms, reporting, and Docker/k3s deployment assets.

This repository is intentionally generic: the demo asset is a **Combined Heat and Power Unit**, but the service model is tenant/site/unit/tag based and can be adapted to other small industrial monitoring use cases.

## Current Architecture At A Glance

- **Frontend:** React 19 + Vite UI, served by nginx in Docker.
- **Public backend boundary:** `Alpha.Scada.Gateway`, a Gateway/BFF that owns public `/api/*` routes and the SignalR realtime hub.
- **Domain services:** Identity, Tenant, Asset, Tag Catalog, Telemetry, Alarm, Reporting, and Edge.
- **Messaging:** NATS Server + JetStream, with Wolverine as the .NET messaging layer for domain events and report jobs.
- **Telemetry ingress:** Edge/device publishers send raw JSON to native NATS subjects such as `alpha.demo-operator.demo-energy-site.chp-demo-001.telemetry`. Telemetry normalizes and persists data before publishing domain events.
- **Historian:** one TimescaleDB/PostgreSQL container in local Compose, with separate logical databases per service.
- **Realtime UI:** Gateway bridges NATS/Wolverine domain events to browser clients over SignalR.
- **Observability:** every .NET service exposes `/health`, `/ready`, and `/metrics`; optional Prometheus/Grafana are available through the Compose `ops` profile.

The detailed architecture, service map, data flows, deployment notes, and diagrams are in [docs/system-overview.md](docs/system-overview.md). Mermaid diagrams are available in a [simplified version](docs/architecture-diagram-simple.mmd) and a [detailed version](docs/architecture-diagram.mmd).

## Core Runtime Flow

1. The optional Edge simulator, or a future field adapter, publishes raw telemetry to native NATS subjects such as `alpha.demo-operator.demo-energy-site.chp-demo-001.telemetry`.
2. Telemetry consumes raw JetStream messages, normalizes them through a protocol adapter, resolves tenant/site/unit/tag metadata, writes current/history rows, and publishes `TelemetryBatchStored`.
3. Asset consumes stored telemetry events to update unit online/last-seen state; Alarm consumes the same events to raise or clear alarms.
4. Gateway consumes telemetry/status/alarm/report events and pushes browser updates through SignalR.
5. Reporting runs as an async NATS job: Gateway publishes `ReportRequested`, Reporting gathers Tag Catalog config plus Asset/Telemetry/Alarm data, stores the report, then publishes `ReportCompleted`.

## Quickstart

Generate local secrets:

```bash
ops/scripts/dev-setup.sh
```

Start the full local stack:

```bash
docker compose up --build
```

Open the browser UI:

```text
http://localhost:8080
```

The Gateway is also exposed directly for API debugging:

```text
http://localhost:5202
```

Local demo credentials live in [docs/dev-setup.md](docs/dev-setup.md). Demo user seeding is enabled in Docker Compose only; production-like startup should not use `Seed__DemoUsers=true`.

## Useful Commands

Run backend build and tests:

```bash
dotnet build Alpha.Scada.slnx
dotnet test Alpha.Scada.slnx
```

Build the frontend:

```bash
cd src/Alpha.Scada.Web
npm install
npm run build
```

Start optional observability services:

```bash
docker compose --profile ops up --build
```

Grafana is then available at:

```text
http://localhost:3000
```

## Repository Map

| Path | Purpose |
| --- | --- |
| `src/Alpha.Scada.Web` | React/Vite frontend and nginx container config |
| `src/Alpha.Scada.Gateway` | public API/BFF and SignalR realtime hub |
| `src/Alpha.Scada.Identity` | local users, roles, PBKDF2 password hashing, JWT issuing |
| `src/Alpha.Scada.Tenant` | tenant records and tenant-key resolution |
| `src/Alpha.Scada.Asset` | sites, units, unit status, stale-unit detection |
| `src/Alpha.Scada.TagCatalog` | tag definitions, thresholds, report ontology/config |
| `src/Alpha.Scada.Telemetry` | raw telemetry normalization, current values, TimescaleDB history |
| `src/Alpha.Scada.Alarm` | threshold/quality/communication-loss alarms and alarm lifecycle |
| `src/Alpha.Scada.Reporting` | monthly report generation and report persistence |
| `src/Alpha.Scada.Edge` | optional development simulator / edge publishing boundary |
| `src/Alpha.Scada.ServiceDefaults` | shared auth, HTTP clients, database, metrics, Wolverine/NATS conventions |
| `src/*Contracts` | shared DTOs and domain event records |
| `ops` | Compose support files, k3s manifests, NATS/Postgres config, Prometheus/Grafana, backup scripts |
| `tests/Alpha.Scada.Tests` | unit and integration tests, including NATS/Postgres Testcontainers coverage |

## Documentation

- [System overview](docs/system-overview.md)
- [Simplified architecture diagram](docs/architecture-diagram-simple.mmd)
- [Detailed architecture diagram](docs/architecture-diagram.mmd)
- [Messaging ADR](docs/architecture-decisions/002-messaging.md)
- [Messaging runbook](docs/messaging-runbook.md)
- [Developer setup](docs/dev-setup.md)
- [Sparkplug B integration task](docs/tasks/sparkplug-b-integration.md)

## Important Notes

- Local Compose uses one TimescaleDB/PostgreSQL container with separate service databases. It is not production HA.
- Service migrators currently run at application startup.
- NATS development credentials in `ops/nats/nats-server.conf` and k3s manifests are placeholders and must be replaced for a real deployment.
- Sparkplug B is not implemented yet. NATS reserves `spBv1.0.>` as an ingress-ready subject, and the task plan is documented in [docs/tasks/sparkplug-b-integration.md](docs/tasks/sparkplug-b-integration.md).
