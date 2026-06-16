# Alpha SCADA Simplified Architecture Diagram Notes

> Companion to [`architecture-review.md`](architecture-review.md) and the detailed diagram set in [`architecture-diagram-notes.md`](architecture-diagram-notes.md).
> This version keeps only the major runtime responsibilities and is intended for stakeholder review.

```mermaid
flowchart LR
    classDef actor fill:#e8f5ff,stroke:#2563eb,color:#0f172a
    classDef boundary fill:#f5f3ff,stroke:#7c3aed,color:#0f172a
    classDef service fill:#f8fafc,stroke:#64748b,color:#0f172a
    classDef broker fill:#eefdf5,stroke:#059669,color:#0f172a
    classDef data fill:#fff7ed,stroke:#ea580c,color:#0f172a
    classDef ops fill:#f0fdfa,stroke:#0891b2,color:#0f172a

    Browser["Operator browser<br/>React UI"]:::actor
    Edge["Edge devices / simulator<br/>raw telemetry publisher"]:::actor

    Gateway["Gateway / BFF<br/>public REST API + SignalR"]:::boundary
    Telemetry["Telemetry service<br/>adapter boundary + historian"]:::service
    Domain["Domain services<br/>Identity, Tenant, Asset,<br/>TagCatalog, Alarm, Reporting"]:::service
    Nats["NATS JetStream<br/>raw telemetry, domain events,<br/>report jobs"]:::broker
    Data["TimescaleDB / PostgreSQL<br/>database per service"]:::data
    Ops["Prometheus + Grafana<br/>health, metrics, dashboards"]:::ops

    Browser -->|"API requests"| Gateway
    Gateway -.->|"live SignalR updates"| Browser

    Edge -->|"alpha.tenant.site.unit.telemetry"| Nats
    Nats -->|"raw telemetry stream"| Telemetry
    Telemetry -->|"persist current + history"| Data
    Telemetry -->|"TelemetryBatchStored"| Nats

    Nats -->|"stored telemetry events"| Domain
    Domain -->|"owned state"| Data
    Domain -->|"status, alarms, reports"| Nats

    Gateway -->|"queries + commands"| Domain
    Gateway -->|"telemetry/history queries"| Telemetry
    Gateway -->|"report request jobs"| Nats
    Nats -->|"events for UI fan-out"| Gateway

    Ops -->|"scrape / inspect"| Gateway
    Ops -->|"scrape / inspect"| Domain
    Ops -->|"stream health"| Nats
```

## How To Read It

- **Gateway** is the public backend boundary. The browser never calls internal services directly.
- **Telemetry** is the protocol normalization boundary. Raw edge payloads become canonical telemetry before other services see them.
- **NATS JetStream** carries raw telemetry, normalized domain events, and asynchronous report jobs.
- **TimescaleDB/PostgreSQL** remains the system of record. NATS is the durable transport, not the historian.
- **Domain services** own their own data and react to normalized events instead of parsing edge payloads.
