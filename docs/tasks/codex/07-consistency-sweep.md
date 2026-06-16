# Task 07 — Consistency sweep + ephemeral realtime broadcast listeners

Read `README.md` in this folder first for repo context.

## Goal

One branch, **two commits**:

- **Commit 1 (Part A — zero behavior change):** the C# codebase uses one style per concern — primary constructors everywhere, one cancellation-token idiom in endpoint lambdas, one private-field naming convention, and typed PascalCase records (not mixed-casing anonymous objects) for SignalR payloads.
- **Commit 2 (Part B — behavior change):** the Gateway's high-frequency realtime broadcast listeners stop using durable JetStream consumers + the durable Postgres inbox, and become ephemeral in-memory listeners. Only `ReportCompleted` stays durable.

Part A is mechanical and must be reviewable as "no semantic diff". Part B changes delivery semantics deliberately and carries its own verification.

---

## Part A — Style consistency (commit 1)

### A1. Repository constructor style → primary constructors

Three repositories use explicit `private readonly NpgsqlDataSource dataSource;` + a constructor; the other four (Identity, Reporting, TagCatalog, Tenant) already use primary constructors. Convert the three to match:

- `src/Alpha.Scada.Telemetry/Infrastructure/TelemetryRepository.cs` (~lines 8–15)
- `src/Alpha.Scada.Alarm/Infrastructure/AlarmRepository.cs` (~lines 10–17)
- `src/Alpha.Scada.Asset/Infrastructure/AssetRepository.cs` (~lines 8–15)

Target shape: `public sealed class TelemetryRepository(NpgsqlDataSource dataSource)`. The parameter name matches the existing field name, so method bodies don't change — delete the field and constructor, nothing else.

Do **not** touch `TelemetryMigrator` (it legitimately has two constructors) or any class where a primary constructor would force other changes.

### A2. Private-field naming → no underscore prefix

The codebase convention is unprefixed private fields (`gate`, `wakeups`, `inFlight`, `principalId`, …). Two files deviate:

- `src/Alpha.Scada.ServiceDefaults/JwtTokenService.cs`: `_handler`, `_signingKey`, `_validationParameters`
- `src/Alpha.Scada.Edge/Application/ChpUnitSimulatorWorker.cs`: `_random`

Rename (IDE-style rename only; no other edits in those files).

### A3. One cancellation idiom in endpoint lambdas

Minimal APIs bind a `CancellationToken` parameter to `HttpContext.RequestAborted` automatically. Several domain-service endpoints instead bind `HttpContext context` **solely** to pass `context.RequestAborted`. Replace the `HttpContext` parameter with `CancellationToken cancellationToken` in these endpoints (the `AuthenticatedUser` parameter keeps working — its `BindAsync(HttpContext)` receives the context from the framework regardless):

| File | Endpoints |
|---|---|
| `src/Alpha.Scada.Identity/Program.cs` | `POST /auth/logout` |
| `src/Alpha.Scada.Tenant/Program.cs` | `GET /tenants` |
| `src/Alpha.Scada.TagCatalog/Program.cs` | `GET /units/{unitId}/tags` |
| `src/Alpha.Scada.Asset/Program.cs` | `GET /sites`, `GET /sites/{siteId}/units`, `GET /units/{unitId}` |
| `src/Alpha.Scada.Alarm/Program.cs` | `GET /alarms/active`, `POST /alarms/{alarmId}/ack` |
| `src/Alpha.Scada.Telemetry/Program.cs` | `GET /telemetry/units/{unitId}/current`, `GET /telemetry/tags/{tagId}/history` |
| `src/Alpha.Scada.Reporting/Program.cs` | `GET /reports/monthly` |

**Leave `src/Alpha.Scada.Gateway/Program.cs` alone**: its endpoints genuinely need `HttpContext` for `WithBearerToken(context)` token forwarding, so `context.RequestAborted` is the consistent choice *there*. After this change, `grep -rn "context.RequestAborted" src/*/Program.cs` must match only the Gateway.

### A4. SignalR payloads → typed records, one casing

The four broadcast handlers in `src/Alpha.Scada.Gateway/Application/` currently mix styles: `TelemetryBroadcastHandler` hand-writes camelCase anonymous properties (`tenantId`, `samples`…), while `UnitStatusBroadcastHandler`, `AlarmBroadcastHandler`, and `ReportCompletedHandler` use PascalCase member shorthand. The wire format is identical either way **only because** SignalR's default JSON hub protocol camelCases property names — the source inconsistency is a trap.

Replace all four anonymous objects with sealed records in `src/Alpha.Scada.Gateway/Realtime/` (one file, e.g. `BroadcastPayloads.cs`), PascalCase properties, mirroring the **exact current wire fields**:

```csharp
public sealed record TelemetryUpdatedPayload(Guid TenantId, Guid UnitId, DateTimeOffset StoredAtUtc, IReadOnlyList<TelemetrySamplePayload> Samples);
public sealed record TelemetrySamplePayload(Guid TagId, string TagKey, double Value, string Quality, DateTimeOffset TimestampUtc);
public sealed record UnitStatusChangedPayload(Guid TenantId, Guid UnitId, string Status, DateTimeOffset? LastSeenUtc);
public sealed record AlarmsChangedPayload(Guid TenantId, Guid UnitId);
public sealed record ReportCompletedPayload(Guid RequestId, Guid ReportId, Guid TenantId, Guid UnitId, string Period, DateTimeOffset CompletedAtUtc);
```

Careful with field names: the telemetry sample's wire field is `timestampUtc` but it is sourced from `sample.SourceTimestampUtc` — keep the payload property named `TimestampUtc` so the wire name does not change. The frontend (`src/Alpha.Scada.Web/src/api/types.ts`, `useSignalR.ts`) must require **zero changes** in this part.

Check `tests/Alpha.Scada.Tests/AlarmBroadcastTests.cs`, `StatusBroadcastTests.cs`, and `RealtimeTenantIsolationTests.cs` first — if they assert on the anonymous payloads via reflection/serialization, update them to the records (asserting the same wire-level property names/values).

### Part A acceptance

- `dotnet build` — 0 warnings; full test suite green.
- No public HTTP/SignalR contract change: run the stack and confirm live tag values, alarm banner, and unit status still update in the UI.
- `grep -rn "private readonly NpgsqlDataSource" src` → no matches; `grep -rn "_random\|_handler\b\|_signingKey" src` → no matches; `grep -rn "context.RequestAborted" src/*/Program.cs` → Gateway only; `grep -rn "SendAsync(\"" src/Alpha.Scada.Gateway` shows records, no anonymous objects.

---

## Part B — Ephemeral realtime broadcast listeners (commit 2)

### Problem

`UseDurableInboxOnAllListeners()` (`src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessaging.cs:65`) applies to the Gateway's six listeners (`src/Alpha.Scada.Gateway/MessagingTopology.cs`), whose handlers only push to SignalR. Every 1 Hz `TelemetryBatchStored` is therefore written to the Gateway's Postgres inbox (~86k rows/day/unit) before a transient UI push, and after Gateway downtime the durable JetStream consumers + inbox **replay the backlog as stale pushes**. Realtime fan-out wants at-most-once/latest-only.

### Implementation

1. **New topology helper** in `src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessagingTopology.cs`:

   ```csharp
   public static WolverineOptions ListenForEphemeralDomainEvent(this WolverineOptions options, string subject)
   {
       options.ListenToNatsSubject(subject).BufferedInMemory();
       return options;
   }
   ```

   Mechanics, verified against the installed packages (Wolverine.Nats 5.21): `ListenToNatsSubject(subject)` **without** `.UseJetStream(...)` is a plain core NATS subscription — no durable consumer, no replay; it still receives the events live because a JetStream publish is a normal NATS publish that the stream *also* captures. `BufferedInMemory()` exists on `IListenerConfiguration` and overrides the durable-inbox policy for this endpoint (explicit endpoint configuration beats global policies in Wolverine — confirm via the acceptance query below).

2. **Switch five listeners** in `src/Alpha.Scada.Gateway/MessagingTopology.cs` to `ListenForEphemeralDomainEvent`: `TelemetryStoredEvent`, `StatusChangedEvent`, `AlarmRaisedEvent`, `AlarmClearedEvent`, `AlarmAcknowledgedEvent`. **Keep `ReportCompleted` durable** (`ListenForDomainEvent(..., "gateway-report-completed")`) — a missed completion would leave the UI spinner stuck, and replay-on-restart un-sticks it. The `PublishReportRequest` side is untouched.

3. **Frontend reconnect refetch** (compensates for events missed while disconnected — a pre-existing gap that becomes load-bearing now). In `src/Alpha.Scada.Web/src/hooks/useSignalR.ts`, on `onreconnected` also refresh data:

   ```ts
   connection.onreconnected(() => {
     setStatus("Live");
     void handlers.current.loadAlarms();
     void handlers.current.loadSitesAndUnits();
   });
   ```

   (`loadSitesAndUnits` cascades into reloading units and current tag values.)

4. **Orphaned durable consumers**: the five now-unused durables (`gateway-telemetry-stored`, `gateway-status`, `gateway-alarm-raised`, `gateway-alarm-cleared`, `gateway-alarm-acknowledged`) remain on `ALPHA_DOMAIN` and just lag forever. Add a short "After upgrading" note to `docs/messaging-runbook.md` with the cleanup commands (`nats consumer rm ALPHA_DOMAIN gateway-telemetry-stored -f`, etc. — **not** `gateway-report-completed`).

5. **Tests**: read `tests/Alpha.Scada.Tests/MessagingBootstrapTests.cs` and `RealtimeTenantIsolationTests.cs` before changing anything — if they assert the existence of the gateway durable consumers or rely on replay, update them to the new semantics. Note for the isolation tests: core subscriptions only receive messages published *after* the listener is up; Wolverine starts listeners during host `StartAsync`, and the tests already start the host before publishing — keep that ordering.

### Part B acceptance

- With the compose stack running and the simulator ingesting: `docker compose exec postgres psql -U alpha -d alpha_gateway -c "select count(*) from wolverine.wolverine_incoming_envelopes"` stays flat (no growth) over a few minutes, while the UI continues to live-update.
- `docker compose stop gateway`, wait ~2 minutes, `docker compose start gateway`: the UI reconnects, refreshes alarms/units once, and shows only *new* telemetry — no replay burst of stale samples.
- Trigger a monthly report from the Reports screen → completes, `reportCompleted` still refreshes the list (durable path intact).
- Full test suite green.

---

## Constraints

- Part A must not change any HTTP route, response shape, SignalR event name, or wire-level JSON property name.
- Part B must not change subjects, payloads, the `ALPHA_JOBS` publishing path, or anything in the Alarm service's outbox.
- Don't reformat unrelated code; keep diffs surgical so commit 1 reviews as pure mechanics.
- No new packages.

## Verification

```bash
dotnet build Alpha.Scada.slnx          # 0 warnings
dotnet test Alpha.Scada.slnx           # green
docker compose up --build              # manual checks from Part A + Part B acceptance above
```

In the PR description, list the grep outputs from Part A acceptance and the before/after `wolverine_incoming_envelopes` counts from Part B.
