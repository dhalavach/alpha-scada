# Alpha SCADA — Architecture Diagrams (by Claude)

> _Authored by Claude (Opus 4.8) on 2026-06-05 (codebase @ `fc21040`)._
> _Companion to [`ARCHITECTURE-by-Claude.md`](ARCHITECTURE-by-Claude.md). GitHub renders the Mermaid blocks below._
> _For a first-pass stakeholder view, use [`architecture-diagram-by-Claude-simple.md`](architecture-diagram-by-Claude-simple.md)._

---

## 1. System context & components

```mermaid
flowchart TB
    subgraph clients["Clients"]
        UI["React 19 SPA<br/>(nginx :8080)"]
        DEV["Edge devices /<br/>Edge simulator"]
    end

    subgraph edgeplane["Public boundary"]
        GW["Gateway / BFF :5202<br/>REST /api/* + SignalR hub<br/>JWT validation, CORS, fan-out"]
    end

    subgraph services["Domain services (internal :8080)"]
        ID["Identity<br/>users, JWT"]
        TEN["Tenant"]
        AS["Asset<br/>sites/units/status"]
        TC["TagCatalog<br/>tags, thresholds, report profiles"]
        TEL["Telemetry<br/>adapter + historian"]
        AL["Alarm<br/>evaluation + outbox"]
        REP["Reporting<br/>monthly reports"]
    end

    subgraph platform["Platform"]
        NATS["NATS + JetStream<br/>:4222 / mqtt :1883 / mon :8222<br/>streams: ALPHA_EDGE, ALPHA_DOMAIN, ALPHA_JOBS"]
        PG["PostgreSQL / TimescaleDB :5432<br/>8 logical DBs + wolverine schema"]
    end

    subgraph ops["Observability (ops profile)"]
        PROM["Prometheus"]
        GRAF["Grafana :3000"]
    end

    UI -->|HTTPS REST + WS| GW
    DEV -->|raw JSON telemetry| NATS
    GW -->|REST reads + bearer fwd| ID & TEN & AS & TC & TEL & AL & REP
    GW <-->|events| NATS

    ID --- PG
    TEN --- PG
    AS --- PG
    TC --- PG
    TEL --- PG
    AL --- PG
    REP --- PG

    AS <--> NATS
    TEL <--> NATS
    AL <--> NATS
    REP <--> NATS

    PROM -->|scrape /metrics| GW & ID & TEN & AS & TC & TEL & AL & REP
    GRAF --> PROM
```

---

## 2. Messaging topology (who publishes / who listens)

```mermaid
flowchart LR
    EDGE["Edge / simulator"]

    subgraph S_EDGE["JetStream: ALPHA_EDGE (raw)"]
        T_TEL["alpha.*.*.*.telemetry"]
        T_SPB["spBv1.0.&gt; (reserved)"]
    end

    subgraph S_DOMAIN["JetStream: ALPHA_DOMAIN (events)"]
        E_STORED["alpha.telemetry.stored"]
        E_STATUS["alpha.status.changed"]
        E_ARAISE["alpha.alarm.raised"]
        E_ACLEAR["alpha.alarm.cleared"]
        E_AACK["alpha.alarm.acknowledged"]
        E_RDONE["alpha.report.completed"]
    end

    subgraph S_JOBS["JetStream: ALPHA_JOBS (work queue)"]
        J_REQ["alpha.report.requested"]
    end

    EDGE -->|publish| T_TEL
    T_TEL -->|native adapter worker| TEL["Telemetry"]
    TEL -->|publish| E_STORED

    E_STORED --> AS["Asset"]
    E_STORED --> AL["Alarm"]
    E_STORED --> GW["Gateway"]

    AS -->|publish| E_STATUS
    E_STATUS --> AL
    E_STATUS --> GW

    AL -->|outbox dispatch| E_ARAISE & E_ACLEAR & E_AACK
    E_ARAISE & E_ACLEAR & E_AACK --> GW

    GW -->|publish| J_REQ
    J_REQ --> REP["Reporting"]
    REP -->|publish| E_RDONE
    E_RDONE --> GW
```

---

## 3. Telemetry ingestion (anti-corruption boundary) — sequence

```mermaid
sequenceDiagram
    autonumber
    participant Edge as Edge device/simulator
    participant JS as JetStream (ALPHA_EDGE)
    participant W as TelemetryAdapterIngestionWorker
    participant A as NatsJsonTelemetryAdapter
    participant H as CanonicalTelemetryHandler
    participant Cat as CatalogCache (→ Tenant/Asset/TagCatalog)
    participant DB as TimescaleDB
    participant Bus as Wolverine outbox → ALPHA_DOMAIN

    Edge->>JS: publish raw JSON to alpha.t.s.u.telemetry (+ Nats-Msg-Id)
    JS->>W: deliver message (durable consumer)
    W->>A: Normalize(payload, {subject, headers})
    A-->>W: CanonicalTelemetry {tenant,site,unit,readings} (or throws → poison)
    W->>H: Handle(CanonicalTelemetry)
    H->>Cat: resolve tenant/unit/tag ids (1-min cache)
    Cat-->>H: ids (unresolved tags dropped — see roadmap)
    H->>DB: IngestAsync (set-based unnest, on conflict do nothing)
    H-->>W: TelemetryBatchStored
    W->>Bus: PublishAsync(TelemetryBatchStored, dedup=Nats-Msg-Id)
    W->>JS: Ack

    Note over W,JS: malformed JSON / bad schema → dead-letter (AckTerminate) immediately<br/>other errors → Nak (redelivery, MaxDeliver 5)
```

---

## 4. Alarm lifecycle with guaranteed delivery (transactional outbox)

```mermaid
sequenceDiagram
    autonumber
    participant Bus as ALPHA_DOMAIN (telemetry.stored)
    participant AH as TelemetryStoredAlarmHandler
    participant Svc as AlarmService
    participant DB as alpha_alarm DB
    participant Disp as AlarmOutboxDispatcher
    participant Out as Wolverine → ALPHA_DOMAIN
    participant GW as Gateway (SignalR)

    Bus->>AH: TelemetryBatchStored
    AH->>Svc: EvaluateAsync(samples)
    Svc->>DB: BEGIN; insert alarm_events (RETURNING) + insert alarm_outbox; COMMIT
    Note over Svc,DB: state change and event row committed atomically
    Svc-->>AH: events
    AH->>Disp: Kick() (opportunistic)
    Disp->>DB: SELECT ... FOR UPDATE SKIP LOCKED (pending, attempts < max)
    Disp->>Out: PublishAsync(AlarmRaised/Cleared, dedup id)
    Disp->>DB: mark dispatched
    Out->>GW: AlarmRaised/Cleared
    GW->>GW: broadcast to tenant SignalR group
    Note over Disp: 1s sweeper recovers anything missed by a crash
```

---

## 5. Monthly reporting (async job)

```mermaid
sequenceDiagram
    autonumber
    participant UI as Browser
    participant GW as Gateway
    participant JOBS as ALPHA_JOBS
    participant REP as Reporting
    participant TC as TagCatalog
    participant TEL as Telemetry (CAGG)
    participant AL as Alarm
    participant DOM as ALPHA_DOMAIN

    UI->>GW: POST /api/reports/monthly/run
    GW->>JOBS: publish ReportRequested
    JOBS->>REP: deliver (work queue)
    REP->>TC: GET report profile (metric bindings)
    REP->>TEL: POST report-aggregate (per-minute CAGG)
    REP->>AL: GET alarm count for period
    REP->>REP: compose + persist MonthlyReport
    REP->>DOM: publish ReportCompleted
    DOM->>GW: ReportCompleted
    GW->>UI: SignalR notify / list refresh
```

---

## 6. Telemetry & alarm data model (TimescaleDB)

```mermaid
erDiagram
    telemetry_samples {
        uuid tag_id PK
        timestamptz timestamp_utc PK
        uuid tenant_id
        uuid unit_id
        text tag_key
        double value_double
        text quality
        timestamptz source_timestamp_utc
        timestamptz received_at_utc
    }
    tag_current {
        uuid tag_id PK
        uuid tenant_id
        uuid unit_id
        text tag_key
        double value_double
        text quality
        timestamptz timestamp_utc
    }
    telemetry_minute {
        uuid tag_id
        timestamptz minute_utc
        double value_avg
    }
    alarm_events {
        uuid id PK
        uuid tenant_id
        uuid unit_id
        uuid tag_id
        text severity
        text message
        text state
        timestamptz raised_at_utc
        timestamptz acknowledged_at_utc
        timestamptz cleared_at_utc
    }
    alarm_outbox {
        uuid id PK
        text event_type
        jsonb payload
        timestamptz occurred_at_utc
        timestamptz dispatched_at_utc
        int attempts
        text last_error
    }

    telemetry_samples ||..o{ telemetry_minute : "1-min avg rollup (CAGG)"
    telemetry_samples ||--|| tag_current : "latest value per tag"
    alarm_events ||--o{ alarm_outbox : "emitted in same tx"
```

> `telemetry_samples` is a hypertable (1-day chunks, compress >7d, retention 365d). `alarm_events` has a partial unique index on `tag_id WHERE state IN ('active','acknowledged')` enforcing one active alarm per tag.

---

## 7. Deployment (local Docker Compose)

```mermaid
flowchart TB
    subgraph host["Docker host"]
        FE["frontend :8080 (nginx)"]
        GW["gateway :5202"]
        subgraph internal["network: alpha-internal"]
            ID["identity"]:::svc
            TEN["tenant"]:::svc
            AS["asset"]:::svc
            TC["tag-catalog"]:::svc
            TEL["telemetry"]:::svc
            AL["alarm"]:::svc
            REP["reporting"]:::svc
            EDGE["edge (simulator)"]:::svc
            NATS["nats :4222/:1883/:8222"]:::infra
            PG["postgres/timescaledb :5432"]:::infra
        end
        subgraph opsp["profile: ops"]
            PROM["prometheus"]:::infra
            GRAF["grafana :3000"]:::infra
        end
    end

    FE --> GW
    GW --> internal
    classDef svc fill:#eef,stroke:#557;
    classDef infra fill:#efe,stroke:#575;
```

> Only `frontend (8080)` and `gateway (5202)` (plus `grafana 3000` under the ops profile) expose host ports; everything else is reachable only on `alpha-internal`. In k3s (`ops/k3s/`) the same topology runs as Deployments/StatefulSets with NATS and Postgres as cluster services.
