# Floyd F60 SCADA MVP

Lightweight `.NET 10` MVP scaffold for Floyd F60 monitoring.

Current slice:

- ASP.NET Core backend.
- In-memory F60 simulator.
- REST endpoints for units, current tags, history, active alarms, and monthly report summary.
- Native WebSocket telemetry stream at `/ws/telemetry`.
- Static browser HMI served by the backend.
- Docker Compose profile with PostgreSQL and Mosquitto ready for real adapters.

## Run Locally

```bash
dotnet run --project src/Floyd.Scada.Api/Floyd.Scada.Api.csproj
```

Open:

```text
http://localhost:5202
```

The port may differ if `dotnet run` chooses another launch profile. The console output prints the active URL.

## Run With Compose

```bash
docker compose up --build
```

Open:

```text
http://localhost:8080
```

## API

```text
GET /health
GET /api/units
GET /api/tags/current
GET /api/tags/{tagKey}/history?minutes=30
GET /api/alarms/active
GET /api/reports/monthly
WS  /ws/telemetry
```

## Next Implementation Steps

1. Add real MQTT ingestion using MQTTnet.
2. Add PostgreSQL persistence using Npgsql.
3. Replace simulator-only data with Node-RED MQTT publishes.
4. Add local user login.
5. Add PDF generation for the monthly report.
