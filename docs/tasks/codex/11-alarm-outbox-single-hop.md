# Task 11 — Alarm events: one persistence hop, fixed dispatcher defects

Read `README.md` in this folder first for repo context.

## Goal

An alarm event is persisted **once** between the `alarm_events` transaction and NATS, atomically with the state change; the outbox table stops growing without bound; publishes no longer happen inside an open database transaction; poisoned rows are observable.

## Problem

Alarm events are persisted twice: `AlarmRepository.EnqueueOutboxAsync` writes to the custom `alarm_outbox` inside the domain transaction (correct), then `AlarmOutboxDispatcher.DispatchPendingAsync` publishes via `bus.PublishAsync(...)`, which lands every event in **Wolverine's durable outbox** for a second persisted hop before NATS. Additional defects in `src/Alpha.Scada.Alarm/Application/AlarmOutboxDispatcher.cs`:

1. `DispatchPendingAsync` holds the `for update skip locked` transaction open across all publishes in the batch — row locks held during network I/O (lines 58–78).
2. Dispatched rows are **never deleted** — `alarm_outbox` grows forever.
3. Rows that exhaust `MaxAttempts` are abandoned with only a log line — invisible to metrics/alerting.

## Design note — read before coding

The obvious "use Wolverine cascading messages instead" fix is **not** automatically atomic here: the repositories open their own Npgsql connections/transactions, so Wolverine would persist cascaded messages in *its* transaction after the handler's repo transaction already committed — a crash in between loses the event, and on redelivery the (idempotent) evaluation produces no new events, so the loss is permanent. Making cascading atomic would require routing all alarm DB work through Wolverine-managed transactions — a much larger refactor.

Therefore the chosen design **keeps the custom `alarm_outbox` as the single outbox** (it already has the right atomicity) and removes the *second* hop by publishing directly to NATS JetStream from the dispatcher, exactly like the telemetry DLQ publish does.

## Implementation steps

1. **Publish directly to JetStream from the dispatcher.** Replace `bus.PublishAsync(...)` in `AlarmOutboxDispatcher` with a raw publish using `NATS.Client.Core` + `NATS.Client.JetStream` (the Alarm csproj may need these package references — Telemetry already has them; with central package management from Task 08 this is two lines):
   - Build a `NatsConnection` from `NatsOptions.FromConfiguration` the same way `TelemetryAdapterIngestionWorker.BuildNatsOptions` does (consider extracting that small builder into `ServiceDefaults.Messaging` since this is its third copy-shape).
   - Subject per event type: `Topics.AlarmRaisedEvent` / `AlarmClearedEvent` / `AlarmAcknowledgedEvent`.
   - Serialize with the same `JsonSerializerDefaults.Web` options Wolverine uses (`AlarmOutboxEvents.JsonOptions` already matches) — **wire compatibility check**: Wolverine listeners deserialize incoming NATS messages by subject-routed message type with web-default JSON; verify against an existing consumer test (`AlarmBroadcastTests` / gateway listener) that a raw-published payload still deserializes into `AlarmRaised` etc. If Wolverine's NATS envelope requires specific headers for message-type resolution, set them the same way the existing telemetry → `TelemetryBatchStored` interop does; the integration test below is the arbiter.
   - Set the `Nats-Msg-Id` header to the outbox row id (preserves the existing dedup semantics within the stream's duplicate window) and check the PubAck (`ack.EnsureSuccess()`).
2. **Stop holding the transaction across publishes.** Restructure `DispatchPendingAsync`: transaction 1 — `select ... for update skip locked` the batch **and commit immediately** after marking rows `claimed_at_utc = now()` (add the column) so other replicas skip them; then publish outside any transaction; then transaction 2 — mark dispatched / failed per row. Rows claimed but never marked (process crash mid-publish) must be reclaimed: treat `claimed_at_utc < now() - interval '2 minutes'` as unclaimed in the select predicate. Simpler alternative (acceptable given single-replica deployment): keep the single-transaction shape but publish after commit and mark dispatched in a second transaction, accepting at-least-once republish on crash (dedup header absorbs it). Choose one, document it in a comment.
3. **Bound the table.** In the sweep loop, after dispatching: `delete from alarm_outbox where dispatched_at_utc < now() - interval '7 days'`. Add an index touch-up migration if needed (`003_alarm_outbox_claims` for the new column + any index change).
4. **Expose poison rows.** Add an `IAlphaMetricsProvider` to the Alarm service (pattern: `TelemetryIngestionMetrics`) emitting `alpha_scada_alarm_outbox_poison_total` (rows with `attempts >= MaxAttempts and dispatched_at_utc is null`) and `alpha_scada_alarm_outbox_pending` (undispatched count), sampled at scrape time with one cheap indexed query (the partial index `ix_alarm_outbox_pending` already exists). Register it in `Program.cs`.
5. **Remove the second hop's leftovers**: the Alarm service's Wolverine `PublishDomainEvent<AlarmRaised/Cleared/Acknowledged>` routes in `src/Alpha.Scada.Alarm/MessagingTopology.cs` become dead once nothing calls `bus.PublishAsync` for them — delete the three `PublishDomainEvent` lines (keep the two listeners). `IMessageBus` can be dropped from the dispatcher's constructor.

## Tests

- Update `tests/Alpha.Scada.Tests/AlarmOutboxTests.cs` (3 facts) to the new dispatcher mechanics: dispatch publishes to NATS (assert via `NatsTestSupport.WaitForSubjectAsync` on the alarm subject), marks dispatched, deletes old dispatched rows, and leaves poison rows counted.
- End-to-end consumer compatibility: extend `CommunicationLossAlarmTests` (or a new fact) so the consuming side — a Wolverine listener host on `Topics.AlarmRaisedEvent` — receives and **deserializes** the raw-published `AlarmRaised` into the typed handler. This is the proof the raw publish is wire-compatible with Wolverine consumers (gateway broadcasts).
- NATS-outage recovery: with the NATS container stopped, ack an alarm → outbox row stays pending; start NATS → row dispatches within the sweep interval, event observed exactly once.

## Constraints

- Subjects, payloads, and consumer behavior (gateway SignalR broadcasts) unchanged.
- `EnqueueOutboxAsync` and the enqueue-in-domain-transaction pattern stay exactly as-is — that part is correct.
- Single-replica assumptions are fine, but say so in comments where relied upon.

## Verification

```bash
dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
docker compose up --build
# Raise an alarm (stop edge for ~2.5 min) and ack it in the UI; then:
docker compose exec postgres psql -U alpha -d alpha_alarm -c "select count(*) from alarm_outbox where dispatched_at_utc is not null"  # bounded, eventually pruned
# Wolverine outbox in alpha_alarm no longer receives alarm events:
docker compose exec postgres psql -U alpha -d alpha_alarm -c "select count(*) from wolverine.wolverine_outgoing_envelopes"            # stays 0/flat
# Gateway still pushes alarmsChanged (UI alarm banner updates live).
```
