# ADR 002: Wolverine Messaging With MQTT And PostgreSQL

## Status

Accepted for implementation.

## Decision

Alpha SCADA will use Wolverine as the .NET messaging abstraction. Mosquitto remains the MQTT broker for edge telemetry and operational event topics, while PostgreSQL backs Wolverine durable command queues, inboxes, outboxes, retries, and error queues.

We will not add a custom `Alpha.Scada.Messaging` abstraction above Wolverine. Application code may use Wolverine messaging APIs directly, with shared bootstrapping centralized in `Alpha.Scada.ServiceDefaults`.

## Context

The current system has multiple services communicating mostly through synchronous HTTP. Edge telemetry ingestion is especially coupled: Edge resolves tenant, asset, and tag metadata, writes telemetry, triggers alarm evaluation, updates status, and calls Gateway realtime callbacks.

This creates brittle service orchestration and weak recovery semantics. We need durable asynchronous command/event flows without adding another broker container or adopting EF Core.

## Why Wolverine

Wolverine provides the application messaging layer, handler discovery, retries, durable inbox/outbox, PostgreSQL-backed queues, and MQTT transport support. Its PostgreSQL transport lets us add durable work queues using the database technology already present in the stack instead of introducing a second broker for internal commands and jobs.

Wolverine's MQTT transport currently requires MQTT v5. Raw Edge telemetry is not Wolverine-enveloped JSON, so Telemetry's raw MQTT listener must explicitly map incoming MQTT payloads to the existing telemetry envelope shape. Wolverine-native outbound messages may use Wolverine envelopes because all downstream consumers are Wolverine-aware.

## Why Not Azure Service Bus, RabbitMQ, Or NATS

Azure Service Bus is a good future option for Azure-hosted production environments, but it adds a cloud dependency and operational model we do not need for the pilot. RabbitMQ and NATS would add another broker container and another operational surface while the current scale can be handled by Mosquitto plus PostgreSQL-backed Wolverine queues.

We will revisit the broker choice if telemetry volume, customer deployment constraints, cloud platform requirements, or team ownership boundaries justify the extra infrastructure.

## Messaging Boundaries

Telemetry is the normalization boundary. Raw MQTT envelopes from Edge are integration events and are consumed only by Telemetry.

Telemetry validates schema version, timestamp freshness, quality, tenant/site/unit keys, and tag keys. After data is resolved and persisted, Telemetry publishes `TelemetryBatchStored` as the downstream domain event.

Alarm, Asset, Gateway, and Reporting must not parse raw Edge telemetry. They consume domain commands/events such as `TelemetryBatchStored`, `AlarmRaised`, `UnitStatusChanged`, `ReportRequested`, and `ReportCompleted`.

## Accepted Tradeoff

Telemetry outages stall alarm evaluation because downstream alarm processing waits for `TelemetryBatchStored`. Wolverine durable inboxes and queues preserve the work until Telemetry recovers.

We accept this coupled failure mode because the alternative, where Alarm independently processes raw telemetry, can create operator-visible drift: alarms referencing values that were never persisted to telemetry history.

## Delivery Guarantee

Messaging is at-least-once. Consumers must be idempotent, and durable inbox tracking is required for message handlers that mutate state.

When a service writes database state and publishes a resulting event, the write and outgoing message must use Wolverine's durable outbox when the service owns a database transaction for that operation.

## Ordering

For v1, raw telemetry ordering is preserved per MQTT topic by publishing one telemetry stream per tenant/site/unit topic and running a single consumer instance per service. Horizontal scaling of telemetry consumers is deferred because it requires partitioning by `unitId`, likely through MQTT 5 shared subscriptions or another partitioned transport strategy.

## Schema Evolution

Message contracts carry a schema version. Additive changes are allowed within the same major version.

Consumers process the same major/minor version. Consumers may process higher minor versions when the change is additive, but must log a warning. Consumers must route different major versions to the Wolverine error queue or MQTT DLQ rather than attempting to process them.

Breaking changes require a new message type and a transition window during which publishers emit both old and new messages.

## DLQ And Error Policy

For Wolverine PostgreSQL queues, failed messages move to Wolverine's error queue after the configured retry policy is exhausted. For MQTT interop failures, bad messages move to `alpha/_dlq/{service}/{original-topic}` where supported by the listener/handler path.

Operational policy is manual triage with alerting. DLQ or error queue depth greater than zero for more than five minutes is actionable.

## What Stays HTTP

Frontend-facing `/api/*` reads stay HTTP through the Gateway because the browser needs request/response query semantics. Internal low-volume lookup/query endpoints also stay HTTP where they are naturally queries, such as Telemetry resolving catalog data or Alarm refreshing threshold metadata.

Reporting may continue to query Asset, Telemetry, and Alarm over HTTP for low-volume report fan-in. Report generation itself becomes an asynchronous command through Wolverine.

## Gateway Database Decision

Gateway does not get its own database in v1. It publishes `ReportRequested` directly to Reporting's PostgreSQL-backed Wolverine queue because the Gateway has no local state mutation that needs a transactional outbox.

If Gateway later owns durable state or publishes more critical commands, we will add an `alpha_gateway` database and enable a Gateway outbox.

## Edge Role After Migration

Edge remains the OT/adapter boundary and optional simulator host. After telemetry fan-out is removed, its core responsibility is publishing edge/device telemetry to Mosquitto using the agreed MQTT contract.

If no real edge adapter exists in a deployment, Edge may be reduced to a simulator-only service or replaced by Node-RED/device adapters that publish directly to Mosquitto.

## Wolverine Schema Management

For v1, services may allow Wolverine to create or update its own persistence schema at startup in their service databases. If application database migrations are later moved out of startup, Wolverine schema changes must move into the same migration pipeline.

## Consequences

The system keeps the low-cost Docker Compose footprint while gaining durable messaging and clearer service boundaries. Telemetry becomes the single source of truth for raw envelope parsing and resolution, reducing drift when validation rules, tag metadata, or future protocols such as Sparkplug B change.

The tradeoff is stronger dependence on Wolverine and PostgreSQL-backed queues. That is acceptable for the pilot because it removes broker sprawl and keeps operations simple.
