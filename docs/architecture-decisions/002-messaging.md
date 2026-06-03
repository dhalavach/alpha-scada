# ADR 002: Wolverine Messaging Over NATS JetStream

## Status

Accepted for implementation.

## Decision

Alpha SCADA uses NATS Server with JetStream as the durable messaging backbone. Wolverine remains the .NET application messaging layer for handlers, inbox/outbox, retries, and error handling; we do not add a custom `Alpha.Scada.Messaging` abstraction above Wolverine.

NATS also exposes the edge-facing MQTT listener. Edge adapters publish slash-topic MQTT payloads such as `alpha/{tenant}/{site}/{unit}/telemetry`; NATS maps them into dot-separated subjects such as `alpha.{tenant}.{site}.{unit}.telemetry`.

## Why NATS

NATS gives the platform one broker for edge ingress, domain events, and background jobs. JetStream provides durable streams and work-queue semantics without making PostgreSQL the broker.

This is a better foundation for Sparkplug B because NATS can accept MQTT topics under `spBv1.0/#` while internal services consume normalized NATS subjects after Telemetry validates and persists the payload.

## Why Wolverine

Wolverine provides handler discovery, retry policy, durable inbox/outbox, transactional publishing, and consistent service bootstrapping. PostgreSQL persistence remains enabled only for Wolverine local durability/error metadata where required; PostgreSQL queue transport is no longer used.

## Messaging Boundaries

Telemetry is the normalization boundary. Raw edge envelopes are integration events and are consumed only by Telemetry.

Telemetry validates schema version, tenant/site/unit keys, tag keys, quality, and timestamps. After the data is resolved and persisted, Telemetry publishes `TelemetryBatchStored` as a domain event.

Alarm, Asset, Gateway, and Reporting must not parse raw edge telemetry. They consume domain commands/events such as `TelemetryBatchStored`, `UnitStatusChanged`, `AlarmRaised`, `ReportRequested`, and `ReportCompleted`.

## Streams And Subjects

- `ALPHA_EDGE`: `alpha.*.*.*.telemetry`, `spBv1.0.>`
- `ALPHA_DOMAIN`: `alpha.telemetry.stored`, `alpha.status.changed`, `alpha.alarm.*`, `alpha.report.completed`
- `ALPHA_JOBS`: `alpha.report.requested`

Report requests use JetStream work-queue semantics. Domain events use log-style streams so multiple services can independently consume the same event. Tenant, site, and unit routing details are carried in the event payload rather than encoded into every Wolverine-native subject.

## Accepted Tradeoff

Telemetry outages stall alarm evaluation because downstream alarm processing waits for `TelemetryBatchStored`. Durable NATS/JetStream storage and Wolverine inboxes preserve the work until Telemetry recovers.

We accept this coupled failure mode because the alternative, where Alarm independently processes raw telemetry, can create operator-visible drift: alarms referencing values that were never persisted to telemetry history.

## Delivery Guarantee

Messaging is at-least-once. Consumers must be idempotent, and durable inbox tracking is required for message handlers that mutate state.

When a service writes database state and publishes a resulting event, the write and outgoing message should use Wolverine's durable outbox when the service owns a database transaction for that operation.

## Ordering

For v1, raw telemetry ordering is preserved per unit subject by publishing one stream of messages per `alpha.{tenant}.{site}.{unit}.telemetry` subject and running a single Telemetry consumer instance. Horizontal scaling of telemetry consumers is deferred until we partition by unit identity.

## Schema Evolution

Message contracts carry a schema version. Additive changes are allowed within the same major version.

Consumers process the same major/minor version. Consumers may process higher minor versions when the change is additive, but must log a warning. Consumers must reject different major versions rather than attempting to process them.

Breaking changes require a new message type and a transition window during which publishers emit both old and new messages.

## DLQ And Error Policy

Wolverine-handled failures move to Wolverine's error queue after the configured retry policy is exhausted. Raw telemetry failures are ack-terminated for invalid schema/payload and nacked for transient service failures so JetStream redelivers them.

Operational policy is manual triage with alerting. Error queue or failed-message depth greater than zero for more than five minutes is actionable.

## What Stays HTTP

Frontend-facing `/api/*` reads stay HTTP through the Gateway because the browser needs request/response query semantics. Internal low-volume lookup/query endpoints also stay HTTP where they are naturally queries, such as Telemetry resolving catalog data or Alarm refreshing threshold metadata.

Reporting may continue to query Asset, Telemetry, and Alarm over HTTP for low-volume report fan-in. Report generation itself is an asynchronous command over NATS.

## Edge Role

Edge remains the OT/adapter boundary and optional simulator host. Its core responsibility is publishing edge/device telemetry to the NATS MQTT listener using the agreed telemetry contract.

If no real edge adapter exists in a deployment, Edge may be reduced to a simulator-only service or replaced by Node-RED/device adapters that publish directly to NATS' MQTT listener.

## Wolverine Schema Management

For v1, services may allow Wolverine to create or update its own persistence schema at startup in their service databases. If application database migrations are later moved out of startup, Wolverine schema changes must move into the same migration pipeline.
