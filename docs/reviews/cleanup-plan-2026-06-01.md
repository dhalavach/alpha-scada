# Alpha SCADA — Cleanup Plan

**Date:** 2026-06-01
**Author:** automated architecture review
**Goal:** Remove AI-slop artifacts (dead scaffolding, duplicated/parallel mechanisms) so the
codebase reads as a deliberately lightweight, senior-built project — without changing any
behavior that is actually in use. Architecture-simplification (monolith) is called out
separately as a decision, not bundled into the safe cleanups.

> Status legend: **[SAFE]** = no behavior change, low risk, do now · **[FIX]** = changes
> runtime behavior (closes a real gap) · **[DECISION]** = needs your call before work starts.

---

## Phase 0 — Branch & baseline

1. Branch off `main`: `git checkout -b chore/deslop`.
2. Capture a green baseline so every later step is checked against it:
   ```bash
   dotnet build Alpha.Scada.slnx
   dotnet test  Alpha.Scada.slnx
   ```
3. Commit per phase below (one logical commit each) so review/rollback is granular.

---

## Phase 1 — Delete the dead "shadow" pipeline  **[SAFE]**

**Why:** Verified zero callers anywhere in `src/` or `tests/` (only the definitions
themselves match). It is leftover from the HTTP→MQTT migration. It is still created on every
startup (costs DDL time, confuses readers) and was even re-optimized in the batch-write merge —
effort spent on code nothing runs. No test references "shadow", so removal is safe.

**Remove:**

| File | What to remove |
|---|---|
| `src/Alpha.Scada.Telemetry/Infrastructure/TelemetryRepository.cs` | `IngestShadowAsync` (line ~14) |
| `src/Alpha.Scada.Alarm/Infrastructure/AlarmRepository.cs` | `EvaluateShadowAsync` (line ~15) |
| `src/Alpha.Scada.Asset/Infrastructure/AssetRepository.cs` | `MarkShadowSeenAsync` (line ~124) |
| `src/Alpha.Scada.ServiceDefaults/Messaging/Topics.cs` | `ShadowTelemetryStored(...)` (line 23) and `ShadowTelemetryStoredWildcard` (line 30) |
| `src/Alpha.Scada.Telemetry/Infrastructure/TelemetryMigrator.cs` | `tag_current_shadow` + `telemetry_samples_shadow` (+ default partition + index), lines ~37–61 |
| `src/Alpha.Scada.Alarm/Infrastructure/AlarmMigrator.cs` | `alarm_events_shadow` table + its 3 indexes, lines ~31–47 |
| `src/Alpha.Scada.Asset/Infrastructure/AssetMigrator.cs` | `unit_last_seen_shadow` (line ~50) |
| `ops/scripts/compare-shadow.sql` | delete the file |

**Refactor note:** after removing the shadow callers, the `IngestIntoAsync(samplesTable,
currentTable, …)` / `EvaluateIntoAsync(tableName, …)` table-name *parameters* become single-use.
Inline the real table names (`telemetry_samples` / `tag_current` / `alarm_events`) and drop the
parameter so the SQL is no longer string-built from a variable. Net result: simpler methods, no
dynamic table name in SQL.

**Migration caveat (call out, don't auto-run):** dropping the `create table if not exists`
statements stops *new* DBs from creating shadow tables, but does **not** drop them from existing
dev/pilot databases. Add a one-line note to the messaging runbook, or ship a throwaway
`drop table if exists … cascade` snippet for anyone with an existing volume. For local dev the
simplest path is recreating the Postgres volume.

**Verify:** `grep -rin shadow src tests ops` returns nothing; build + tests green.

---

## Phase 2 — Consolidate the auth helper  **[SAFE]**

**Why:** Three functions do the same "parse Bearer → validate → `CurrentUserDto`":
`GatewayAuth.Authenticate`, `HttpUserContext.FromBearerToken`, and `MinimalApi.RequireUser`
(the last is defined but used by **no one**). `GatewayAuth.Authenticate` is a near-verbatim copy
of `FromBearerToken`.

**Keep one, delete two:**
- **Keep** `HttpUserContext.FromBearerToken` as the single token→user parser (already used by all
  6 domain services).
- **Delete** `GatewayAuth.Authenticate`; in `Gateway/Program.cs` (~13 call sites) replace
  `GatewayAuth.Authenticate(context, tokens)` with
  `HttpUserContext.FromBearerToken(context.Request.Headers, tokens)`.
- **Delete** the unused `MinimalApi.RequireUser` *or* actually adopt it everywhere — pick one.
  Recommendation: delete it; the inline `FromBearerToken` + null-check is already consistent and
  readable, and `RequireUser`'s callback shape never caught on.

**Do NOT touch:** `GatewayAuth.WithBearerToken` / `HttpUserContext.ForwardAuthorizationFrom` /
`ForwardAuthorization` — these are the **token-forwarding** helpers and are actively used to pass
the bearer token to internal services. Only the *authenticate/parse* duplicate is dead.

**Optional follow-up (still [SAFE]):** the Gateway configures a full
`AddAuthentication().AddJwtBearer()` + `UseAuthorization()` pipeline that no REST endpoint uses
(only the SignalR hub's `[Authorize]` exercises it). Either remove the unused JwtBearer
registration, or migrate endpoints to `.RequireAuthorization()` and drop the manual calls. Defer
unless you want to settle on one model now — flag for a separate commit.

**Verify:** `grep -rn "GatewayAuth.Authenticate\|RequireUser" src` returns nothing; build + tests green.

---

## Phase 3 — Remove orphaned / stray files  **[SAFE]**

1. **`src/Alpha.Scada.Api/`** — orphan directory: not in `Alpha.Scada.slnx`, no git-tracked
   files, only stale pre-rename API build artifacts. Delete the directory.
2. **Stale pre-rename build artifacts** — untracked `bin/`/`obj/` leftovers. Harmless but tidy:
   `dotnet clean` then delete stray legacy-named `obj/Debug` artifacts if they persist. (Git-ignored, so
   repo is unaffected either way.)
3. **`docs/tasks/sparkplug-b-integration.md`** (untracked, 162 lines) — a full design doc for a
   feature that is explicitly on the "deferred / known limitations" list. Recommend: move to a
   GitHub issue or a `design/` branch rather than committing speculative planning into the repo.
   *(Your call — see Decision D3.)*

**Verify:** `dotnet build Alpha.Scada.slnx` still green; solution unchanged.

---

## Phase 4 — Fold single-file contract projects  **[SAFE, optional]**

**Why:** 5 contract assemblies, two holding a single file
(`Alpha.Scada.Asset.Contracts` → `UnitStatusChanged.cs`,
`Alpha.Scada.Telemetry.Contracts` → `TelemetryBatchStored.cs`). A whole `.csproj`/assembly per
event type is over-engineering at ~3,600 LOC.

**Options (pick in Decision D2):**
- **4a (minimal):** merge the two single-file projects into the shared `Alpha.Scada.Contracts`;
  update the handful of `ProjectReference`s and `using`s. Keeps `Alarm.Contracts` /
  `Reporting.Contracts` (which hold 2–3 related messages) as-is.
- **4b (full):** collapse *all* contracts into `Alpha.Scada.Contracts` with namespaced folders
  (`Messaging/Alarm`, `Messaging/Reporting`, …). Fewer build units, one place for contracts.

**Verify:** build + tests green; no dangling `ProjectReference`.

---

## Phase 5 — Realtime fan-out + tenant isolation  **[FIX]**  *(behavior change)*

**Why:** This is the one item that is both dead-code *and* a live correctness/security gap, so
it's separated from the pure-cleanup phases. All four broadcast handlers use `hub.Clients.All`,
while `TelemetryHub.JoinTenant/JoinUnit` (which would scope delivery) are **never called**.
Result: every authenticated client receives every tenant's unit IDs and event timing
(cross-tenant metadata leak), plus an O(N·M) refetch storm.

**Change:**
- In `Gateway/Realtime/TelemetryHub.cs`: on `OnConnectedAsync`, auto-join the caller's tenant
  group from **JWT claims** (`tenant:{claimTenantId}`). Do **not** accept client-supplied tenant
  IDs — delete the parameterized `JoinTenant(string)`/`JoinUnit(string)` (they let a client
  subscribe to any group).
- In the four handlers (`TelemetryBroadcastHandler`, `AlarmBroadcastHandler`,
  `UnitStatusBroadcastHandler`, `ReportCompletedHandler`): replace
  `hub.Clients.All.SendAsync(...)` with `hub.Clients.Group($"tenant:{tenantId}").SendAsync(...)`.
- Frontend `useSignalR.ts`: no change needed if the server auto-joins from claims; confirm it
  doesn't rely on calling `JoinTenant`.

**Why not [SAFE]:** it changes who receives messages. Needs a quick manual check that the UI
still updates after the group switch (one operator logged in, telemetry/alarm/report events
arrive). Recommend its **own commit + a tenant-isolation regression test** (two tenants, assert
tenant A never receives tenant B's events) — that test would have caught this originally.

---

## Phase 6 — Minor doc accuracy  **[SAFE]**

- Re-check README / `system-overview.md` for the "outbox/event-bus deferred" wording flagged
  previously — a durable Postgres outbox **is** implemented in `AlphaMessaging`. Fix any
  remaining "deferred" claim that no longer holds.
- Soften the `system-overview.md` audience framing if you want it to read less like an enterprise
  deliverable ("intended for engineers, solution architects, delivery leads, and operators").
  Cosmetic; skip if you don't care.

---

## DECISION — Architecture simplification  **[DECISION]**  *(not in scope of the cleanup branch)*

The biggest "not built by a human optimizing for simple" signal is structural: **8 microservices
+ Gateway + 5 contract assemblies for ~3,600 lines of C#** — 8 databases, HTTP `/internal/v1`
hops on the hot ingestion path, per-service migrators, 15 build units — without the
team/scaling pressure that justifies it (`Tenant` is a 153-line "service").

This is a **separate, larger effort** and should not be mixed into the de-slop branch. If you
want to pursue it, I'll write a dedicated migration plan. Sketch of the target:

- One ASP.NET host; modules as folders/class libs (`Identity`, `Tenant`, `Asset`, `TagCatalog`,
  `Telemetry`, `Alarm`, `Reporting`).
- One Postgres database, **schema-per-module**; migrators become module initializers.
- In-process messaging (Wolverine local queues / mediator) for what is currently HTTP+MQTT
  between services; **keep MQTT only at the true edge** (Telemetry ingestion from Node-RED).
- Most contract assemblies collapse into internal namespaces.
- Net: drop ~7 deployables and 7 databases, keep the clean domain boundaries; data-ownership
  discipline means you could still split back out later if real scaling pressure appears.

**This is reversible-direction and high-leverage but invasive.** Treat it as Phase 2 of the
overall effort, after the safe cleanups land.

---

## Decisions I need from you before starting

- **D1 — Phase 5 (realtime fix):** include it in this cleanup branch, or split to its own PR?
  (It's the only behavior change here.)
- **D2 — Phase 4 (contracts):** option 4a (merge only the two single-file projects) or 4b
  (collapse all)?
- **D3 — sparkplug doc:** delete / move to issue / keep in repo?
- **D4 — Monolith decision:** do you want a separate migration plan written, or is the
  microservice split staying for now?

## Suggested commit sequence (safe cleanups only)

```
chore/deslop
├─ 1. remove dead shadow pipeline (code + topics + migrator DDL + compare-shadow.sql)
├─ 2. consolidate auth helper (delete GatewayAuth.Authenticate + RequireUser)
├─ 3. remove orphaned Alpha.Scada.Api + stray files
├─ 4. fold single-file contract projects        (pending D2)
├─ 5. tenant-scoped SignalR groups + iso test    (pending D1)
└─ 6. doc accuracy pass
```

Each commit: `dotnet build` + `dotnet test` green before the next.
```
```

## Expected impact

- **Deletes:** ~3 dead methods, 2 dead topics, ~5 shadow tables/indexes across 3 migrators,
  1 SQL script, 1 orphan project dir, 1 duplicate auth method, 1 unused helper, up to 2 contract
  assemblies.
- **Behavior change:** exactly one (Phase 5), gated behind your D1 decision and a new test.
- **No change to:** SQL parameterization, PBKDF2/JWT, tenant scoping, the merged batch-write
  paths, atomic alarm dedup, or the honest deferral list — all of which are good and stay.
