# Task 03 — Make the telemetry dead-letter queue durable and replayable

Read `README.md` in this folder first for repo context.

## Goal

Dead-lettered telemetry envelopes are captured in a JetStream stream (`ALPHA_DLQ`), include the original payload so they can be inspected and replayed, and messages that silently exhaust redelivery are at least observable.

## Problem

`TelemetryAdapterIngestionWorker.DeadLetterAsync` (`src/Alpha.Scada.Telemetry/Application/TelemetryAdapterIngestionWorker.cs:184–215`) publishes dead letters to `Topics.Dlq("telemetry", subject)` → `alpha._dlq.telemetry.{originalSubject}` using **core NATS** (`connection.PublishAsync`). No JetStream stream binds `alpha._dlq.>` (see `AlphaMessaging.ConfigureNats`, which only defines ALPHA_EDGE / ALPHA_DOMAIN / ALPHA_JOBS) and nothing subscribes — so **every dead letter is dropped by the broker at publish time**. `docs/architecture-review.md` line ~213 documents this as a known gap.

Two secondary gaps:
1. The `DeadLetteredTelemetry` record (bottom of the worker file) contains subject, message id, error type/message, timestamp — but **not the payload**. Even a captured record would be unreplayable.
2. Messages that fail with transient-looking errors get NAK'd and, after `MaxDeliver = 5` (consumer config in `CreateConsumerWhenReadyAsync`), JetStream just stops delivering — no record anywhere.

## Implementation steps

### 1. Define the DLQ stream

`src/Alpha.Scada.ServiceDefaults/Messaging/Topics.cs`:
```csharp
public const string DlqStream = "ALPHA_DLQ";
public const string DlqWildcard = "alpha._dlq.>";
```

`src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessaging.cs`, in `ConfigureNats` next to the existing `DefineLogStream` calls:
```csharp
nats.DefineLogStream(Topics.DlqStream, TimeSpan.FromDays(30), Topics.DlqWildcard);
```
(Wolverine declares the streams at host start, the same way the other three appear today.)

### 2. Publish dead letters through JetStream with the payload

In `TelemetryAdapterIngestionWorker`:
- `ExecuteAsync` already creates `var jetStream = new NatsJSContextFactory().CreateContext(connection);`. Thread the `INatsJSContext` down instead of (or in addition to) the raw `NatsConnection` — change `ProcessOneMeasuredAsync`/`ProcessAsync`/`DeadLetterAsync` signatures accordingly.
- In `DeadLetterAsync`, replace `connection.PublishAsync(...)` with a JetStream publish and check the ack (consult the NATS.Net `INatsJSContext.PublishAsync` return type — `PubAckResponse`; treat an error/exception as failure so control falls through to the existing catch that returns `TelemetryIngestionOutcome.TerminalError` and lets AckWait redeliver).
- Extend the record:
```csharp
private sealed record DeadLetteredTelemetry(
    string Subject,
    string MessageId,
    string ErrorType,
    string ErrorMessage,
    DateTimeOffset DeadLetteredAtUtc,
    string PayloadBase64,
    bool PayloadTruncated);
```
  Populate from `message.Data` with a 64 KB cap: if `Data.Length > 65536`, base64 the first 64 KB and set `PayloadTruncated = true`.
- Keep the existing order: publish DLQ record first, then `AckTerminateAsync` — if the DLQ publish fails the message must NOT be terminated.
- Set the `Nats-Msg-Id` header (`RawTelemetryHeaders.NatsMessageId` = the resolved `messageId`) on the DLQ publish so redelivered dead-lettering doesn't duplicate records within the stream's duplicate window.

### 3. Max-deliveries watchdog (observability floor)

In the worker's `ExecuteAsync`, start a background loop (same lifetime as the consume loop, e.g. `Task.Run` guarded like the pump) that core-subscribes to:
```
$JS.EVENT.ADVISORY.CONSUMER.MAX_DELIVERIES.ALPHA_EDGE.telemetry-edge-json
```
On each advisory: log an error with the advisory JSON (it includes `stream_seq`) and increment a new counter on `TelemetryIngestionMetrics` — add a field `maxDeliveriesExhausted`, a public `void RecordMaxDeliveriesExhausted()` (Interlocked), and emit it in `AppendMetrics` as `alpha_scada_telemetry_ingestion_max_deliveries_exhausted_total` (counter), following the exact style of the existing outcome counters.

**Stretch (optional, only if straightforward with the installed NATS.Net version):** on advisory, fetch the original message by `stream_seq` via the stream's direct-get API and republish it to the DLQ subject with `ErrorType = "MaxDeliveriesExhausted"`. If the API requires gymnastics, skip — the log + counter is the acceptance bar.

### 4. Runbook

Update `docs/messaging-runbook.md`: a short "Telemetry DLQ" section — how to list dead letters (`nats stream view ALPHA_DLQ` / `nats consumer add` with the services credentials), how to decode `payloadBase64`, and how to replay (publish the decoded payload back to the original subject with a fresh `Nats-Msg-Id`). Also fix the stale claim in `docs/architecture-review.md` (~line 213) that the DLQ is not durable.

## Tests

Model on `tests/Alpha.Scada.Tests/TelemetryPrimaryIngestionTests.cs` (read it first — it boots a telemetry host with Testcontainers Postgres + NATS and uses `NatsTestSupport`):

1. `Invalid_telemetry_payload_is_dead_lettered_durably`:
   - Boot the host (streams get declared), publish a malformed JSON payload to `alpha.demo.site.unit.telemetry` via `NatsTestSupport.PublishAsync`.
   - Create an ephemeral JetStream consumer on `ALPHA_DLQ` (filter `alpha._dlq.>`) and fetch one message within a timeout.
   - Deserialize and assert: `Subject` matches, `ErrorType` is `JsonException` (or the envelope exception), `PayloadBase64` round-trips to the published bytes, `PayloadTruncated == false`.
2. Assert the stream exists after host start: `js.GetStreamAsync("ALPHA_DLQ")` succeeds.
3. Unit-level: payload > 64 KB → truncated flag set, base64 length corresponds to the cap (this can be a pure function if you extract `DeadLetteredTelemetry` construction into a small static helper — recommended).

## Constraints

- Do not change ack/nak/term semantics for valid messages or the success path.
- Do not introduce new packages — NATS.Client.* is already referenced by the Telemetry project.
- The DLQ record JSON casing stays `JsonSerializerDefaults.Web` (existing `JsonOptions`).
- Stream retention 30 days; do not make it a work queue (multiple inspectors must be able to read it).

## Verification

```bash
dotnet build Alpha.Scada.slnx    # 0 warnings
dotnet test Alpha.Scada.slnx --filter "FullyQualifiedName~DeadLetter|FullyQualifiedName~TelemetryPrimaryIngestion"
# Manual:
docker compose up --build
docker compose exec nats sh -c "nats --user admin --password \"$NATS_PASSWORD_ADMIN\" stream ls" 2>/dev/null || true
#   (or use the nats CLI from the host against localhost:4222) -> ALPHA_DLQ listed
# Publish garbage to a telemetry subject with the edge credentials -> record appears in ALPHA_DLQ,
# telemetry service logs the dead-letter warning, /metrics shows dead_letter outcome incremented.
```
