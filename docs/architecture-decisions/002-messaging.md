# ADR 002: Wolverine Messaging Over NATS JetStream

## Status

Accepted for implementation.

## Decision

Alpha SCADA uses NATS Server with JetStream as the durable messaging backbone. Wolverine remains the .NET application messaging layer for handlers, inbox/outbox, retries, and error handling; we do not add a custom `Alpha.Scada.Messaging` abstraction above Wolverine.

The current Alpha JSON telemetry path uses native NATS subjects such as `alpha.demo-operator.demo-energy-site.chp-demo-001.telemetry`. NATS also exposes an MQTT listener for future external edge adapters and Sparkplug B interop, but MQTT slash topics are no longer the simulator or test path for Alpha JSON telemetry.

## Why NATS

NATS gives the platform one broker for edge ingress, domain events, and background jobs. JetStream provides durable streams and work-queue semantics without making PostgreSQL the broker.

This is a better foundation for Sparkplug B because NATS can reserve ingress under `spBv1.0.>` and its MQTT listener can accept MQTT-originated Sparkplug topics while internal services consume normalized domain events after Telemetry validates and persists the payload.

## Why Wolverine

Wolverine provides handler discovery, retry policy, durable inbox/outbox, transactional publishing, and consistent service bootstrapping. PostgreSQL persistence remains enabled only for Wolverine local durability/error metadata where required; PostgreSQL queue transport is no longer used.

## Messaging Boundaries

Telemetry is the normalization boundary. Raw edge envelopes are integration events and are consumed only by Telemetry.

Telemetry validates schema version, tenant/site/unit keys, tag keys, quality, and timestamps. The current implementation uses a Telemetry adapter worker that reads raw JetStream messages, normalizes them through `ITelemetryAdapter`, persists canonical telemetry, and then publishes `TelemetryBatchStored` as a domain event.

Alarm, Asset, Gateway, and Reporting must not parse raw edge telemetry. They consume domain commands/events such as `TelemetryBatchStored`, `UnitStatusChanged`, `AlarmRaised`, `ReportRequested`, and `ReportCompleted`.

## Streams And Subjects

- `ALPHA_EDGE`: `alpha.*.*.*.telemetry`, `spBv1.0.>`
- `ALPHA_DOMAIN`: `alpha.telemetry.stored`, `alpha.status.changed`, `alpha.alarm.*`
- `ALPHA_REPORTS`: `alpha.report.completed`
- `ALPHA_JOBS`: `alpha.report.requested`

Report requests use JetStream work-queue semantics. Domain events use log-style streams so multiple services can independently consume the same event. Tenant, site, and unit routing details are carried in the event payload rather than encoded into every Wolverine-native subject.

## Accepted Tradeoff

Telemetry outages stall alarm evaluation because downstream alarm processing waits for `TelemetryBatchStored`. Durable NATS/JetStream storage and Wolverine inboxes preserve the work until Telemetry recovers.

We accept this coupled failure mode because the alternative, where Alarm independently processes raw telemetry, can create operator-visible drift: alarms referencing values that were never persisted to telemetry history.

## Delivery Guarantee

Messaging is at-least-once. Consumers must be idempotent, and durable inbox tracking is required for message handlers that mutate state.

Raw telemetry ingestion is deliberately at-least-once with idempotent effects. Telemetry inserts are conflict-tolerant, publishers should set deterministic `Nats-Msg-Id` values, and JetStream duplicate windows reduce duplicate fan-out.

When a service writes database state and publishes a resulting event, prefer Wolverine's supported durable sending behavior. If a service must coordinate raw Npgsql state changes with outgoing event intent and cannot use a supported transactional enrollment, the service-owned outbox must be explicit, tested, and documented. The Alarm service currently uses this pattern with `alarm_outbox`.

## Ordering

For v1, raw telemetry ordering is preserved per unit subject by publishing one stream of messages per `alpha.{tenant}.{site}.{unit}.telemetry` subject and running a single Telemetry consumer instance. Horizontal scaling of telemetry consumers is deferred until we partition by unit identity.

## Schema Evolution

Message contracts carry a schema version. Additive changes are allowed within the same major version.

Consumers process the same major/minor version. Consumers may process higher minor versions when the change is additive, but must log a warning. Consumers must reject different major versions rather than attempting to process them.

Breaking changes require a new message type and a transition window during which publishers emit both old and new messages.

## DLQ And Error Policy

Wolverine-handled failures move to Wolverine's error queue after the configured retry policy is exhausted. Raw telemetry adapter failures are ack-terminated for invalid schema/payload and published to a telemetry dead-letter subject. Transient catalog/database/NATS failures are nacked so JetStream redelivers them.

Operational policy is manual triage with alerting. Error queue or failed-message depth greater than zero for more than five minutes is actionable.

## What Stays HTTP

Frontend-facing `/api/*` reads stay HTTP through the Gateway because the browser needs request/response query semantics. Internal low-volume lookup/query endpoints also stay HTTP where they are naturally queries, such as Telemetry resolving catalog data or Alarm refreshing threshold metadata.

Reporting may continue to query Asset, Telemetry, and Alarm over HTTP for low-volume report fan-in. Report generation itself is an asynchronous command over NATS.

## Edge Role

Edge remains the OT/adapter boundary and optional simulator host. Its current responsibility is publishing edge/device telemetry to native NATS subjects using the agreed Alpha JSON telemetry contract.

If no real edge adapter exists in a deployment, Edge may be reduced to a simulator-only service or replaced by Node-RED/device adapters that publish directly to NATS or through the NATS MQTT listener once an adapter normalizes their payloads.

## Wolverine Schema Management

For v1, services may allow Wolverine to create or update its own persistence schema at startup in their service databases. If application database migrations are later moved out of startup, Wolverine schema changes must move into the same migration pipeline.
