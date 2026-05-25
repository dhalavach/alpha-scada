# Alpha SCADA Platform

Stage 2+3 implementation slice for the Alpha Combined Heat and Power Unit SCADA platform.

Current capabilities:

- ASP.NET Core `.NET 10` modular monolith.
- PostgreSQL-backed tenants, sites, units, tags, current values, history, alarms, users, sessions, audit events, edge devices, and report runs.
- Versioned Node-RED/MQTT telemetry contract through Mosquitto.
- SignalR realtime hub at `/hubs/telemetry`.
- Local user login with PBKDF2 password hashing and fixed roles.
- React/Vite frontend with login, site/unit navigation, Combined Heat and Power Unit overview, alarms, and monthly reports.
- Optional database-backed Combined Heat and Power Unit simulator for local development.
- Docker Compose for local/dev and k3s manifests for production-like deployment.

## Demo Credentials

```text
admin@alpha.local / ChangeMe!123
operator@alpha.local / ChangeMe!123
viewer@alpha.local / ChangeMe!123
support@alpha.local / ChangeMe!123
```

## Run With Compose

```bash
docker compose up --build
```

Open:

```text
http://localhost:8080
```

Optional observability services:

```bash
docker compose --profile ops up --build
```

## Run Backend And Frontend Separately

Start infrastructure:

```bash
docker compose up -d postgres mosquitto
```

Start API:

```bash
dotnet run --project src/Alpha.Scada.Api/Alpha.Scada.Api.csproj --no-launch-profile --urls http://localhost:5202
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
