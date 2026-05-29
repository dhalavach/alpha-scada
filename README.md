# Alpha SCADA Platform

Stage 2+3 implementation slice for the Alpha Combined Heat and Power Unit SCADA platform.

For the detailed architecture, service map, data flows, and code navigation guide, see [docs/system-overview.md](docs/system-overview.md).

Current capabilities:

- ASP.NET Core `.NET 10` Gateway/BFF plus eight Clean Architecture domain services.
- Separate PostgreSQL databases for identity, tenant, asset, tag catalog, edge, telemetry, alarm, and reporting ownership.
- HTTP for request/response queries, plus Wolverine-backed MQTT/PostgreSQL messaging for asynchronous commands and events.
- Versioned Node-RED/MQTT telemetry contract through Mosquitto, normalized by the Telemetry service.
- Gateway-owned SignalR realtime hub at `/hubs/telemetry`.
- Local user login with PBKDF2 password hashing and fixed roles.
- React/Vite frontend with login, site/unit navigation, Combined Heat and Power Unit overview, alarms, and monthly reports.
- Optional Edge-hosted Combined Heat and Power Unit simulator for local development.
- Asset-hosted communication-loss monitor that marks stale units offline; Alarm raises communication-loss alarms from unit status events.
- Docker Compose for local/dev and k3s manifests for production-like deployment.

## Service Layout

```text
Alpha.Scada.Gateway      Public API/BFF, frontend-compatible routes, SignalR
Alpha.Scada.Identity     Users, roles, login, JWT issuing
Alpha.Scada.Tenant       Tenant records and support visibility
Alpha.Scada.Asset        Sites, units, status, key resolution
Alpha.Scada.TagCatalog   Subsystems, tag definitions, units, thresholds
Alpha.Scada.Edge         Optional simulator / adapter host
Alpha.Scada.Telemetry    MQTT normalization, current values, history, aggregates
Alpha.Scada.Alarm        Alarm evaluation, acknowledge, clear lifecycle
Alpha.Scada.Reporting    Monthly report runs and orchestration
```

## Repository Map

```text
src/Alpha.Scada.Gateway          Public API/BFF and SignalR
src/Alpha.Scada.Identity         Local auth, users, roles, JWT issuing
src/Alpha.Scada.Tenant           Tenant lookup and tenant visibility
src/Alpha.Scada.Asset            Sites, units, status, key resolution
src/Alpha.Scada.TagCatalog       Tag definitions and alarm thresholds
src/Alpha.Scada.Edge             Optional simulator / adapter host
src/Alpha.Scada.Telemetry        MQTT normalization, current values, history, aggregates
src/Alpha.Scada.Alarm            Alarm rules and lifecycle
src/Alpha.Scada.Reporting        Monthly report orchestration
src/Alpha.Scada.Contracts        Shared DTOs and role rules
src/Alpha.Scada.ServiceDefaults  Shared database, JWT, and API helpers
src/Alpha.Scada.Web              React/Vite frontend
ops/                             Compose support, k3s, PostgreSQL, Mosquitto, observability
tests/                           xUnit contract and rule tests
```

## Run With Compose

Create local development secrets first. The generated `.env` file is gitignored and provides `JWT_SECRET` for signed user tokens plus Mosquitto credentials for the local services.

```bash
ops/scripts/dev-setup.sh
```

```bash
docker compose up --build
```

Open:

```text
http://localhost:8080
```

Local demo credentials are documented in [docs/dev-setup.md](docs/dev-setup.md); production-mode startup does not seed those accounts.

Only the frontend and Gateway are published to the host in the default Compose stack. Internal services, PostgreSQL, Mosquitto, Prometheus, and Grafana stay on the `alpha-internal` Docker network. For backend debugging, run commands inside the network, for example:

```bash
docker compose exec gateway curl http://alarm:8080/health
docker compose exec postgres psql -U alpha -d alpha_identity
```

If you need direct host ports for a specific backend during local development, add a temporary `docker-compose.override.yml` instead of editing the default stack.

Optional observability services:

```bash
docker compose --profile ops up --build
```

Grafana is available at `http://localhost:3000` in the ops profile. Prometheus stays on the internal Compose network and is queried by Grafana.

## Run Backend And Frontend Separately

Because the default Compose stack no longer exposes PostgreSQL or Mosquitto to the host, use a local override that publishes those ports before running backend services directly on your machine.

Start infrastructure:

```bash
docker compose up -d postgres mosquitto
```

Start services in separate terminals:

```bash
dotnet run --project src/Alpha.Scada.Identity/Alpha.Scada.Identity.csproj --no-launch-profile --urls http://localhost:5210
dotnet run --project src/Alpha.Scada.Tenant/Alpha.Scada.Tenant.csproj --no-launch-profile --urls http://localhost:5211
dotnet run --project src/Alpha.Scada.Asset/Alpha.Scada.Asset.csproj --no-launch-profile --urls http://localhost:5212
dotnet run --project src/Alpha.Scada.TagCatalog/Alpha.Scada.TagCatalog.csproj --no-launch-profile --urls http://localhost:5213
dotnet run --project src/Alpha.Scada.Telemetry/Alpha.Scada.Telemetry.csproj --no-launch-profile --urls http://localhost:5214
dotnet run --project src/Alpha.Scada.Alarm/Alpha.Scada.Alarm.csproj --no-launch-profile --urls http://localhost:5215
dotnet run --project src/Alpha.Scada.Reporting/Alpha.Scada.Reporting.csproj --no-launch-profile --urls http://localhost:5216
dotnet run --project src/Alpha.Scada.Gateway/Alpha.Scada.Gateway.csproj --no-launch-profile --urls http://localhost:5202
dotnet run --project src/Alpha.Scada.Edge/Alpha.Scada.Edge.csproj --no-launch-profile --urls http://localhost:5217
```

Start frontend:

```bash
cd src/Alpha.Scada.Web
npm install
npm run dev
```

Open:

```text
http://localhost:5173
```

## Edge MQTT Contract

Node-RED publishes telemetry to:

```text
alpha/{tenantKey}/{siteKey}/{unitKey}/telemetry
```

Payload:

```json
{
  "schemaVersion": "1.0",
  "unitKey": "chp-demo-001",
  "timestampUtc": "2026-05-25T12:00:00Z",
  "samples": [
    {
      "tagKey": "engine.electrical_output_kw",
      "value": 58.2,
      "quality": "good",
      "sourceTimestampUtc": "2026-05-25T12:00:00Z"
    }
  ]
}
```

## API

```text
POST /api/auth/login
POST /api/auth/logout
GET  /api/me
GET  /api/tenants
GET  /api/sites
GET  /api/sites/{siteId}/units
GET  /api/units/{unitId}
GET  /api/units/{unitId}/tags/current
GET  /api/tags/{tagId}/history?minutes=30
GET  /api/alarms/active
POST /api/alarms/{alarmId}/ack
GET  /api/reports/monthly
POST /api/reports/monthly/run
GET  /health
GET  /ready
GET  /metrics
```

## Verification

```bash
dotnet test Alpha.Scada.slnx
cd src/Alpha.Scada.Web && npm run build
docker compose config -q
```

## Deferred

Still intentionally out of scope:

- Sparkplug B.
- TimescaleDB.
- HA clustering.
- Multi-region active-active.
- BESS optimization.
- Carbon-credit MRV.
- Predictive maintenance/fleet learning.
- CMMS/ERP/BI integrations.
- Cloud-to-device control or setpoints.
