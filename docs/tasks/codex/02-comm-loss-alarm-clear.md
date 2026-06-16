# Task 02 — Clear communication-loss alarms when a unit comes back online

Read `README.md` in this folder first for repo context.

## Goal

When a unit that was offline starts sending telemetry again, its "communication lost" alarm transitions to `cleared` and an `AlarmCleared` event is published — exactly once per recovery.

## Problem

Communication-loss alarms are rows in `alarm_events` with `tag_id IS NULL`, raised when the Alarm service sees `UnitStatusChanged(status="offline")`:
- Raise path: `src/Alpha.Scada.Alarm/Application/UnitStatusAlarmHandler.cs` → `AlarmService.RaiseCommunicationLostAsync` → `AlarmRepository.RaiseCommunicationLostAsync` (guarded by `where not exists (... tag_id is null and state in ('active','acknowledged'))`).

**No code path ever clears them:**
- Telemetry-driven clearing (`AlarmRepository.EvaluateAsync`, the `update ... where tag_id = any(@tag_ids)` branch) can never match `tag_id IS NULL`.
- `UnitStatusAlarmHandler.Handle` returns early for any status other than `"offline"` — the `"online"` transition is ignored.

Consequences: after a unit recovers, the critical comm-loss alarm stays active (or acknowledged) forever in the UI; and because the raise guard sees the still-open alarm, the **next** real outage raises nothing.

Relevant existing pieces you will reuse:
- `UnitStatusChanged` contract (`src/Alpha.Scada.Asset.Contracts/UnitStatusChanged.cs`): carries `TenantId, SiteId, UnitId, TenantKey, SiteKey, UnitKey, UnitName, Status, ChangedAtUtc, LastSeenUtc` — so the route keys are already in the message, no Asset/Tenant HTTP lookups needed.
- `AlarmCleared` contract: `(AlarmId, TenantId, UnitId, Guid? TagId, TenantKey, SiteKey, UnitKey, ClearedAtUtc)`.
- The Asset service emits `UnitStatusChanged(status="online")` **only on transitions** (`AssetRepository.SetUnitOnlineAsync` filters `previous_status <> 'online'`), so handling it cannot cause per-batch spam.
- Outbox plumbing: `AlarmRepository.EnqueueOutboxAsync(connection, tx, events, ct)` + `IAlarmOutboxSignal.Kick()` — follow the exact pattern of `AlarmService.RaiseCommunicationLostAsync`.

## Implementation steps

### 1. Repository: clear by unit

In `src/Alpha.Scada.Alarm/Infrastructure/AlarmRepository.cs` add (mirroring the style of the existing transaction-overload methods):

```csharp
public async Task<IReadOnlyCollection<AlarmDto>> ClearCommunicationLostAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid unitId,
    CancellationToken cancellationToken)
{
    await using var command = new NpgsqlCommand("""
        update alarm_events
        set state = 'cleared', cleared_at_utc = now()
        where unit_id = @unit_id and tag_id is null and state in ('active', 'acknowledged')
        returning id, tenant_id, unit_id, tag_id, severity, message, state, raised_at_utc, acknowledged_at_utc, cleared_at_utc
        """, connection, transaction);
    command.Parameters.AddWithValue("unit_id", unitId);
    return await ReadAlarmsAsync(command, cancellationToken);
}
```

### 2. Service: orchestrate + outbox

In `src/Alpha.Scada.Alarm/Application/AlarmService.cs` add:

```csharp
public async Task<IReadOnlyCollection<AlarmCleared>> ClearCommunicationLostAsync(
    Guid unitId, UnitRouteKeys route, CancellationToken cancellationToken)
{
    await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
    await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
    var alarms = await repository.ClearCommunicationLostAsync(connection, transaction, unitId, cancellationToken);
    var cleared = alarms.Select(alarm => new AlarmCleared(
        alarm.Id, alarm.TenantId, alarm.UnitId, alarm.TagId,
        route.TenantKey, route.SiteKey, route.UnitKey,
        alarm.ClearedAtUtc ?? DateTimeOffset.UtcNow)).ToArray();
    if (cleared.Length > 0)
    {
        await repository.EnqueueOutboxAsync(connection, transaction, cleared, cancellationToken);
    }
    await transaction.CommitAsync(cancellationToken);
    if (cleared.Length > 0)
    {
        outboxSignal.Kick();
    }
    return cleared;
}
```

### 3. Handler: react to `online`

Rework `src/Alpha.Scada.Alarm/Application/UnitStatusAlarmHandler.cs`:

```csharp
public async Task Handle(UnitStatusChanged message, CancellationToken cancellationToken)
{
    var route = new UnitRouteKeys(message.TenantId, message.UnitId, message.TenantKey, message.SiteKey, message.UnitKey);
    if (string.Equals(message.Status, "offline", StringComparison.OrdinalIgnoreCase))
    {
        await service.RaiseCommunicationLostAsync(/* existing UnitDto construction */, route, cancellationToken);
    }
    else if (string.Equals(message.Status, "online", StringComparison.OrdinalIgnoreCase))
    {
        await service.ClearCommunicationLostAsync(message.UnitId, route, cancellationToken);
    }
}
```

Keep the existing `UnitDto` construction for the raise branch exactly as it is today.

### 4. Idempotency / redelivery check

The handler runs on a durable Wolverine listener and may be redelivered. Verify both branches are idempotent (they are by construction: the raise has the `not exists` guard, the clear's `where state in (...)` matches zero rows the second time → no events). State this in the PR description; no code needed.

## Tests

Extend `tests/Alpha.Scada.Tests/CommunicationLossAlarmTests.cs` (read it fully first — it builds a real Alarm host against Testcontainers Postgres + NATS with a fake Asset/Tenant route server):

1. New fact `Online_unit_status_clears_communication_lost_alarm`:
   - Register the cleared-event publish route in the test host topology: `options.PublishMessage<AlarmCleared>().ToNatsSubject(Topics.AlarmClearedEvent).UseJetStream(Topics.DomainStream);`
   - Publish `UnitStatusChanged(..., "offline", ...)`, await the raise on `Topics.AlarmRaisedEvent` (existing helper `NatsTestSupport.WaitForSubjectAsync`).
   - Publish the same message with `"online"`, await `Topics.AlarmClearedEvent`.
   - Assert DB: `select count(*) from alarm_events where state='cleared' and tag_id is null` = 1, and no row left in `('active','acknowledged')`.
2. Second cycle in the same fact (offline → online again): a **new** alarm is raised and cleared (total cleared = 2), proving recovery re-arms the raise guard.
3. Unit-level test for the acknowledged case if quick: seed an alarm, ack it (`AlarmRepository.AcknowledgeAsync`), then clear → state becomes `cleared`.

## Constraints

- Do not change the alarm outbox mechanism, the threshold-evaluation path, or any contracts.
- Do not add HTTP lookups in the handler — route keys come from the message.
- Severity/message of the raise branch unchanged.

## Verification

```bash
dotnet build Alpha.Scada.slnx     # 0 warnings
dotnet test Alpha.Scada.slnx --filter "FullyQualifiedName~CommunicationLoss"
```

Manual end-to-end (optional but ideal): `docker compose up --build`, stop the edge container (`docker compose stop edge`), wait ~2.5 min (CommunicationLoss:StaleMinutes=2) → comm-loss alarm appears in the UI; `docker compose start edge` → within seconds the alarm disappears from Active Alarms and an `alarmsChanged` push refreshes the list.
