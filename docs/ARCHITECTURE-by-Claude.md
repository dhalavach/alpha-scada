# Alpha SCADA — Architecture (by Claude)

> _Authored by Claude (Opus 4.8) on 2026-06-05 from a full read of the codebase at commit `fc21040`._
> _This is a companion/refresh of [`system-overview.md`](system-overview.md). Where the two disagree, treat this file as the later snapshot but verify against source. Diagrams live in [`architecture-diagram-by-Claude.md`](architecture-diagram-by-Claude.md)._

---

## 1. What this system is

Alpha SCADA is a **multi-tenant remote-monitoring / SCADA platform for distributed industrial energy assets**. The reference asset is a **Combined Heat & Power (CHP) / biomass-gasification unit**, but the domain model is generic: **tenant → site → unit → tag**. It ingests high-frequency telemetry from edge devices, stores it as time-series history, evaluates alarms, pushes live updates to a browser UI, and generates monthly energy/availability reports.

It is a **vertical SaaS product**, not plant-floor HMI software: it is cloud/container-hosted, multi-tenant, and fleet-oriented. That framing drives most of the architecture (multi-tenancy, an ingestion boundary that must accept devices it has never seen, event-driven decoupling).

**Tech baseline:** .NET 10 (C#, minimal APIs), NATS 2.12 + JetStream, Wolverine 5.x (in-process messaging over NATS + PostgreSQL), TimescaleDB 2.17 (PostgreSQL 16), React 19 + Vite frontend, Docker Compose (local) and k3s (cluster) deployment assets.

---

## 2. Architectural style & principles

| Principle | How it shows up |
| --- | --- |
| **Microservices with physical boundaries** | 9 deployable services, each its own process and logical database. Boundaries are enforced by the network and separate deployables (a deliberate choice for a consultancy hand-off context, where module-convention discipline is hard to sustain). |
| **Database-per-service** | Each service owns a logical PostgreSQL database; no cross-service SQL. Cross-service consistency is achieved with events, not shared tables. |
| **Event-driven core** | Services communicate through domain events on NATS JetStream via Wolverine. Synchronous HTTP is used only for request/response reads (gateway → service) and metadata resolution. |
| **Anti-corruption ingestion boundary** | The outside world (edge devices) speaks its own format; a pluggable **adapter** normalizes everything to one internal **canonical telemetry contract**. Nothing protocol-specific leaks past the boundary. |
| **At-least-once + idempotent effects** | The default delivery guarantee. Idempotency keys (DB `on conflict`, JetStream `Nats-Msg-Id` dedup, partial unique indexes) make redelivery safe. |
| **Transactional outbox where loss is unacceptable** | Alarm lifecycle events are written in the same DB transaction as the alarm state, then relayed — guaranteeing emission even across a crash. |
| **Small services, shared platform** | Cross-cutting concerns (auth, HTTP clients, DB, migrations, metrics, messaging conventions) live in `Alpha.Scada.ServiceDefaults` so each service is mostly domain code. |

---

## 3. Service catalog

| Service | Responsibility | Datastore | Consumes (events) | Produces (events) | Notable HTTP |
| --- | --- | --- | --- | --- | --- |
| **Gateway** (`5202`) | Public BFF: REST `/api/*`, SignalR realtime hub, CORS, JWT validation, fan-out of domain events to browsers | `wolverine` storage | `TelemetryBatchStored`, `UnitStatusChanged`, `AlarmRaised/Cleared/Acknowledged`, `ReportCompleted` | `ReportRequested` | `/api/auth/*`, `/api/sites`, `/api/units/{id}`, `/api/tags/{id}/history`, `/api/alarms/active`, `/api/alarms/{id}/ack`, `/api/reports/monthly[/run]`, `/hubs/telemetry` |
| **Identity** | Local users, roles, PBKDF2 password hashing, JWT issuing, auth audit | `alpha_identity` | — | — | `/internal/v1/auth/login`, `/auth/logout` |
| **Tenant** | Tenant records, tenant-key resolution | `alpha_tenant` | — | — | `/internal/v1/tenants`, `/tenants/resolve/{key}` |
| **Asset** | Sites, units, unit status (online/offline), stale-unit detection, route-key lookup | `alpha_asset` | `TelemetryBatchStored` | `UnitStatusChanged` | `/internal/v1/sites`, `/units/{id}`, `/units/resolve`, `/units/{id}/route` |
| **TagCatalog** | Tag definitions, thresholds, report "ontology"/profiles | `alpha_tag_catalog` | — | — | `/internal/v1/tags`, `/tags/resolve`, `/report-config/units/{id}` |
| **Telemetry** | Ingestion boundary (adapter) + historian (TimescaleDB) + current values + monthly aggregates | `alpha_telemetry` | raw edge JSON (NATS, native) | `TelemetryBatchStored` | `/internal/v1/telemetry/units/{id}/current`, `/tags/{id}/history`, `/units/{id}/report-aggregate` |
| **Alarm** | Threshold/quality/comms-loss alarm evaluation, lifecycle (raise/ack/clear), guaranteed event emission via outbox | `alpha_alarm` | `TelemetryBatchStored`, `UnitStatusChanged` | `AlarmRaised`, `AlarmCleared`, `AlarmAcknowledged` | `/internal/v1/alarms/active`, `/alarms/{id}/ack`, `/alarms/count` |
| **Reporting** | Monthly report orchestration (telemetry aggregate + alarm count + profile) and persistence | `alpha_reporting` | `ReportRequested` | `ReportCompleted` | `/internal/v1/reports/monthly` |
| **Edge** | Development CHP simulator / field-publisher boundary (raw telemetry producer) | `alpha_edge` | — | raw edge JSON (NATS) | — |
| **Web** (`8080`) | React 19 + Vite SPA, served by nginx | — | — | — | browser UI |

Shared: **`Alpha.Scada.ServiceDefaults`** (platform) and **`*.Contracts`** projects (DTOs + event records shared between producer and consumer services).

---

## 4. Runtime topology (local Compose)

```
Browser ──▶ frontend (nginx :8080) ──▶ Gateway (:5202 → :8080)
                                          │  REST + SignalR
                                          ▼
        ┌──────────── Wolverine / NATS JetStream (:4222) ────────────┐
        │  identity  tenant  asset  tag-catalog  telemetry  alarm    │
        │  reporting  edge                                           │
        └────────────────────────────────────────────────────────────┘
                    │                                  │
          PostgreSQL/TimescaleDB (:5432)        NATS monitor (:8222), MQTT (:1883)
          (8 logical DBs + wolverine schema)

   Optional ops profile: Prometheus + Grafana (:3000), scraping /metrics
```

**Infrastructure containers:**
- `postgres` — `timescale/timescaledb:2.17.2-pg16`; one container, eight logical databases (created by `ops/postgres/init.sql`), plus a `wolverine` schema per service DB for messaging storage.
- `nats` — `nats:2.12-alpine` with JetStream; client `4222`, **MQTT `1883`** (reserved for future MQTT/Sparkplug devices), monitoring `8222`.
- `prometheus` / `grafana` — `ops` profile only.

**Only the gateway (`5202`) and frontend (`8080`) publish host ports**; every domain service listens on `8080` on the internal Docker network. This network segmentation is currently the only boundary protecting service-to-service endpoints (see §8).

---

## 5. Messaging architecture

Two layers cooperate:
- **NATS JetStream** — the durable transport (streams, durable consumers, acks, redelivery, server-side dedup).
- **Wolverine** — the in-process .NET layer: handler discovery, routing, **durable inbox/outbox**, retries, serialization. Configured once in `ServiceDefaults/Messaging/AlphaMessaging.cs`.

### 5.1 Streams (JetStream)

| Stream | Type | Subjects | Purpose |
| --- | --- | --- | --- |
| `ALPHA_EDGE` | log (7-day) | `alpha.*.*.*.telemetry`, `spBv1.0.>` | Raw inbound edge telemetry (and reserved Sparkplug ingress) |
| `ALPHA_DOMAIN` | log (7-day) | `alpha.telemetry.stored`, `alpha.status.changed`, `alpha.alarm.{raised,cleared,acknowledged}` | Internal domain events |
| `ALPHA_REPORTS` | log (7-day) | `alpha.report.completed` | Durable report completion events |
| `ALPHA_JOBS` | work queue | `alpha.report.requested` | Asynchronous report jobs (competing consumers) |

Global JetStream defaults: `MaxAge` 7 days, `AckWait` 30s, `MaxDeliver` 5, `DuplicateWindow` 10 min. Subjects are **dot-delimited** NATS subjects (`alpha.<tenant>.<site>.<unit>.<type>`); the tenant/site/unit hierarchy doubles as a **Unified-Namespace**-style address.

### 5.2 Two consumption models (important)

1. **Wolverine handlers** (the default for *internal* events). A class with a `Handle(TEvent, …)` method is discovered by Wolverine, runs under the **durable inbox**, and emits results by **returning cascaded messages** (enrolled in the durable outbox). Used by Asset, Alarm, Gateway, Reporting. Errors flow through `OnAnyException().RetryWithCooldown(1s,5s,30s).Then.MoveToErrorQueue()`.

2. **A native NATS ingestion gateway** (the *edge* boundary only). `Telemetry/Application/TelemetryAdapterIngestionWorker` is a `BackgroundService` with its **own** `NatsConnection` and JetStream consumer. It exists because the ingestion edge is an **anti-corruption layer** that transforms arbitrary external payloads and dispatches by adapter — work that does not fit Wolverine's listener model. It hand-rolls ack/nak/terminate and a dead-letter publish. This is a deliberate trade (see §11) and is the one place that runs outside the single Wolverine stack.

Outgoing dedup: `AlphaMessaging.AddNatsDeduplicationHeader` copies Wolverine's `DeliveryOptions.DeduplicationId` into the JetStream `Nats-Msg-Id` header so the 10-minute `DuplicateWindow` collapses duplicate publishes.

---

## 6. Telemetry ingestion pipeline (the anti-corruption boundary)

This is the most carefully designed path. Goal: ingest from devices/protocols the platform has never seen, with one internal contract downstream.

```
Edge device / simulator
  └─ publishes raw JSON to  alpha.<tenant>.<site>.<unit>.telemetry  (+ optional Nats-Msg-Id header)
        │  (no Wolverine knowledge required — body + subject only)
        ▼
NATS JetStream  ALPHA_EDGE
        ▼
TelemetryAdapterIngestionWorker  (native consumer, durable "telemetry-edge-json")
        │  TelemetryAdapterResolver.Normalize(payload, TelemetrySource{subject,headers})
        ▼
NatsJsonTelemetryAdapter  (ITelemetryAdapter)
        │  parses subject → identity, deserializes JSON, validates schema + unit-key
        ▼
CanonicalTelemetry { TenantKey, SiteKey, UnitKey, OccurredAtUtc, Readings[] }   ◀── the canonical contract
        ▼
CanonicalTelemetryHandler
        │  CatalogCache.ResolveAsync → tenant/unit/tag IDs (1-min cached)
        │  TelemetryRepository.IngestAsync  (set-based unnest, on conflict do nothing)
        ▼
publish TelemetryBatchStored  (Wolverine durable outbox → ALPHA_DOMAIN)  then  Ack the JetStream msg
```

**Key properties:**
- **Producers are decoupled** — a device sends only a JSON body + subject (+ optional `Nats-Msg-Id`). No Wolverine headers. Adding a new protocol = implement a new `ITelemetryAdapter` and register it; the resolver picks it via `CanHandle`. Downstream (`CanonicalTelemetry` and below) never changes.
- **Idempotent & at-least-once** — the worker acks only after success; redelivery re-ingests (no-op via `on conflict`) and re-publishes (deduped).
- **Poison handling** — malformed JSON / bad schema / identity mismatch raise typed exceptions (`JsonException`, `InvalidTelemetryEnvelopeException`) → dead-lettered immediately (no pointless retries). Other failures `Nak` for redelivery.
- **Reserved extension point** — `spBv1.0.>` is already a stream subject for a future `SparkplugTelemetryAdapter` (protobuf + BIRTH-driven tag discovery).

---

## 7. Data architecture

### 7.1 Per-service databases
Eight logical databases on one PostgreSQL/TimescaleDB instance (locally). Schemas are owned and created by per-service **versioned migrators** (`ServiceDefaults/DatabaseMigrations.SqlDatabaseMigrator`) that run **at application startup** inside one advisory-locked transaction. Wolverine envelope storage lives in a `wolverine` schema.

### 7.2 Telemetry historian (TimescaleDB)
| Object | Shape | Purpose |
| --- | --- | --- |
| `telemetry_samples` | **hypertable**, 1-day chunks, PK `(tag_id, timestamp_utc)` | Raw time-series history |
| — compression | `compress_segmentby = tag_id`, after 7 days | ~10× storage reduction on cold chunks |
| — retention | `add_retention_policy`, default **365 days** (config `Timescale:RetentionDays`) | Bounded growth |
| `tag_current` | plain upsert table, PK `tag_id` | Latest value per tag (fast "current" reads) |
| `telemetry_minute` | **continuous aggregate** (real-time, `materialized_only=false`) | Per-minute averages backing monthly reports |

Ingestion writes are **set-based** (`insert … select from unnest(...)`), not row-by-row. Database-backed
queue-depth metrics are sampled by background workers instead of running SQL during Prometheus scrapes.

### 7.3 Alarm store (transactional outbox)
| Object | Shape | Purpose |
| --- | --- | --- |
| `alarm_events` | partial unique index `ux_alarm_active_tag` on `tag_id where state in ('active','acknowledged')` | One active alarm per tag; atomic dedupe |
| `alarm_outbox` | `id, event_type, payload jsonb, dispatched_at_utc, attempts, last_error` | Guaranteed event emission (written in the same tx as the alarm) |

---

## 8. Domain event flows & delivery guarantees

| Flow | Path | Guarantee |
| --- | --- | --- |
| **Telemetry** | Edge → `ALPHA_EDGE` → adapter/worker → ingest → `TelemetryBatchStored` (`ALPHA_DOMAIN`) | At-least-once + idempotent ingest + dedup |
| **Unit status** | Asset handles `TelemetryBatchStored` → `SetUnitOnline` → returns `UnitStatusChanged` (Wolverine cascade) | At-least-once; first-emit window exists but **self-heals** (comms-loss monitor + next telemetry) |
| **Comms-loss** | `CommunicationLossMonitorWorker` marks stale units offline → `UnitStatusChanged`; Alarm handles it → raises comms-loss alarm | Periodic (configurable interval/threshold) |
| **Alarm lifecycle** | Alarm handles `TelemetryBatchStored` → evaluates → writes `alarm_events` **+** `alarm_outbox` in one tx → `AlarmOutboxDispatcher` publishes `AlarmRaised/Cleared` | **Guaranteed** (atomic outbox + opportunistic dispatch + sweeper) |
| **Alarm ack** | Gateway `POST /api/alarms/{id}/ack` → Alarm service (role-checked) writes ack + outbox in one tx → dispatcher publishes `AlarmAcknowledged` | **Guaranteed** |
| **Reporting** | Gateway publishes `ReportRequested` (`ALPHA_JOBS`) → Reporting aggregates (telemetry CAGG + alarm count + profile) → persists → `ReportCompleted` (`ALPHA_REPORTS`) → Gateway broadcasts | Durable job + durable event |
| **Realtime UI** | Gateway handlers for telemetry/status/alarm/report events → SignalR groups (per tenant) | Best-effort broadcast; persisted state is authoritative |

---

## 9. Security & multi-tenancy

- **Authentication** — JWT (HS256, `Jwt:Secret`), 12-hour lifetime, issued by Identity. Validated everywhere via `ServiceDefaults/AlphaAuthentication`. The `AuthenticatedUser` minimal-API **parameter binder** materializes `CurrentUserDto` from `HttpContext.User`; endpoints opt in with route-group `RequireAuthorization()`.
- **Realtime** — `TelemetryHub` is `[Authorize]`; clients join a **per-tenant group**; WebSocket auth uses the `access_token` query parameter.
- **Two endpoint tiers** (deliberate):
  - **User tier** — `/api/*` (gateway) and `/internal/v1/*` groups — authenticated and **tenant-scoped** in SQL (`@is_support or tenant_id = @tenant_id`, where support engineers see all tenants).
  - **Service-to-service tier** — resolve/route/count/report-aggregate/tenant-resolve — **anonymous**, trusting the internal network only.
- **Authorization** — `RoleRules` (`Admin`, `Operator`, `Viewer`, `SupportEngineer`). `CanAcknowledge` is enforced on alarm ack; `IsSupport` drives cross-tenant visibility.

**Known security gaps** (acceptable for the current stage; tighten per client — see §11): service-to-service endpoints are unauthenticated (network-trust only); `report-aggregate` is not tenant-scoped; `/metrics` is unauthenticated; `CanManageConfiguration` is defined but unused; a `SERVICE_AUTH_TOKEN` concept is not wired.

---

## 10. Observability & operations

- Every service exposes `/health` (liveness), `/ready` (DB connectivity), and `/metrics` (Prometheus text) via `MapAlphaOperationalEndpoints`.
- OpenTelemetry metrics include ASP.NET Core, outbound HTTP, Wolverine, telemetry-ingestion, and alarm-outbox instruments.
- **Prometheus + Grafana** ship under the Compose `ops` profile (dashboards under `ops/grafana`). Alerts (`ops/prometheus/alerts.yml`) cover Wolverine and alarm-outbox backlog/dead-letter conditions.
- OpenTelemetry traces cover ASP.NET Core, outbound HTTP, Npgsql, Wolverine handlers, telemetry ingestion/catalog resolution, and alarm-outbox dispatch. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to export them.
- **Resilience** — outbound HTTP uses `Microsoft.Extensions.Http.Resilience` (`AddAlphaResilience`: 2s attempt timeout, 10s total, 2 retries, exponential backoff).

---

## 11. Key design decisions & trade-offs

1. **NATS JetStream as the single broker** (replacing an earlier Mosquitto-MQTT + PostgreSQL-queue split). One broker for edge ingress *and* internal events; subjects map to the tenant/site/unit namespace; OSS clustering. NATS's MQTT interface (`:1883`) is reserved for future device connectivity.
2. **Wolverine for internal messaging** (kept over MassTransit). Wolverine has first-class NATS + durable inbox/outbox and is free/OSS; MassTransit lacks NATS/MQTT transports and is moving to a commercial license.
3. **TimescaleDB for the historian** (a PostgreSQL *extension*, not a new datastore). Keeps the raw-Npgsql/set-based stack; adds hypertables, compression, retention, and continuous aggregates — fixing earlier hand-rolled partitioning.
4. **Anti-corruption ingestion adapter** with a canonical contract — so unknown devices/protocols are absorbed at the edge without touching the pipeline.
5. **Native ingestion gateway vs. one Wolverine stack** — the transforming edge runs as a dedicated native consumer rather than a Wolverine listener. This is the deliberate exception to "one stack": a recognized *messaging-gateway* pattern, at the cost of hand-rolled consumer config and a second NATS connection.
6. **Transactional outbox for alarms, idempotency for telemetry** — telemetry events are re-derivable from input (idempotency suffices), but alarm events are derived from DB `RETURNING` rows and would be lost on a crash, so they get an atomic outbox. Right tool per flow.
7. **Microservices for boundary enforcement** — chosen partly because the software is delivered to clients by a consultancy; physical service boundaries resist erosion better than module conventions during hand-off.

---

## 12. Known limitations, risks & roadmap

**Correctness / reliability**
- **Telemetry DLQ is durable for poison messages.** The ingestion gateway publishes invalid envelopes to `ALPHA_DLQ` (`alpha_dlq.>`) with the original payload capped at 64 KB, so operators can inspect and replay them. Persistent *non-poison* failures still rely on JetStream max-delivery advisories plus metrics rather than automatic payload capture.
- **Asset `UnitStatusChanged` first-emit window** — non-atomic (cascade after a separate raw-tx commit). Self-heals today; if status ever needs guaranteed delivery, apply the alarm-outbox pattern to Asset.
- **Report continuous aggregate** — `telemetry_minute` is created with a 3-day refresh window and `with no data`; reports for *past* months or for late-arriving (>3-day) data may under-count until/unless a wider/triggered refresh or one-time backfill is added.

**Security (defer-to-client by design)**
- Service-to-service endpoints unauthenticated; `report-aggregate` not tenant-scoped; `/metrics` open; `CanManageConfiguration` unused.

**Flexibility / completeness**
- **Unknown-tag handling** — `CatalogCache` silently **drops** samples whose tag key is not in the catalog. The next major task is **auto-provision/quarantine** instead of dropping (the schema-flexibility half of "we don't know the tags").
- **Sparkplug B** not implemented (subject reserved; producer/decoder pending).

**Operational**
- Single PostgreSQL container locally (not HA); migrators run at startup; no distributed tracing; hard-coded NATS consumer knobs in the ingestion worker (`AckWait`/`MaxDeliver`/`MaxAckPending`/`NakDelay`/durable name) should move to `NatsOptions`.

**Cleanup**
- Likely-dead symbols to audit: `Topics.EdgeMqtt*`, unused `RawTelemetryHeaders.Wolverine*` constants, `MessageEnvelope<T>`, the "ANNOTATION FOR LEARNING" comment blocks (intentional teaching aids, not production docs).

---

## 13. Glossary

- **Tenant / Site / Unit / Tag** — the domain hierarchy. A *tag* is a single measured signal (e.g., `engine.electrical_output_kw`).
- **Canonical telemetry** — the internal, protocol-neutral telemetry contract every adapter produces.
- **Adapter (ACL)** — a component translating one external protocol/format into canonical telemetry.
- **Domain event** — an immutable record of something that happened. Realtime telemetry/status/alarm events use `ALPHA_DOMAIN`; durable report completion uses `ALPHA_REPORTS`.
- **Outbox** — events written to a DB table in the same transaction as the state change, then relayed, for guaranteed delivery.
- **Inbox** — Wolverine's record of processed incoming messages, enabling safe redelivery/dedup.
- **CAGG** — TimescaleDB continuous aggregate (incrementally maintained rollup).
- **DLQ** — dead-letter queue/subject for messages that cannot be processed.
