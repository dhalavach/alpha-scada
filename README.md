# Alpha SCADA Platform

Alpha SCADA is a lightweight open-source SCADA platform for small industrial energy sites. It provides browser-based monitoring, NATS-backed edge telemetry ingestion, alarms, reporting, and a low-cost Docker/k3s deployment path.

For the detailed architecture, service map, data flows, and code navigation guide, see [docs/system-overview.md](docs/system-overview.md).

## What Is Included

- React/Vite browser UI served through nginx.
- ASP.NET Core `.NET 10` Gateway/BFF plus eight domain services.
- Separate PostgreSQL databases per service in local Compose; telemetry uses the TimescaleDB extension for historian storage.
- NATS Server with JetStream for edge ingress, domain events, and report jobs.
- Wolverine messaging for handlers, inbox/outbox, retries, and error queues.
- Local user auth with signed JWTs, PBKDF2 password hashing, and fixed roles.
- Optional development simulator for a generic Combined Heat and Power Unit.

## Quickstart

Generate local secrets:

```bash
ops/scripts/dev-setup.sh
```

Start the full local stack:

```bash
docker compose up --build
```

Existing local database volumes created before the TimescaleDB switch need either a one-time migration path or a local volume reset before the telemetry service can create hypertables.

Open the UI:

```text
http://localhost:8080
```

Local demo credentials are documented in [docs/dev-setup.md](docs/dev-setup.md). Production-mode startup does not seed those accounts.

## Useful Commands

Run backend tests:

```bash
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

Grafana is available at `http://localhost:3000` in the ops profile.

## More Documentation

- [System overview](docs/system-overview.md)
- [Developer setup](docs/dev-setup.md)
- [Messaging ADR](docs/architecture-decisions/002-messaging.md)
- [Messaging runbook](docs/messaging-runbook.md)
