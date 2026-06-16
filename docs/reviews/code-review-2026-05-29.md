# Alpha SCADA — Code Quality & Architecture Review

**Date:** 2026-05-29
**Reviewer:** automated architecture review
**Scope:** Full source tree (`src/`, `tests/`, `ops/`, docs). ~4,700 LOC C# app code + 1,226 LOC tests + React/Vite frontend. Commit history and the single large commit are explicitly out of scope per request.
**Goal lens:** enterprise-grade (secure, scalable, performant) *and* fairly simple.

---

## 1. Summary Verdict

This is a **well-above-average AI-generated codebase**. It is coherent, internally consistent, idiomatic .NET 10, and genuinely follows the architecture its docs describe — which is *not* typical of "AI slop." Security fundamentals (parameterized SQL, PBKDF2, tenant scoping, durable messaging) are done correctly and consistently.

The warning signs that *do* exist are not careless mistakes but the classic generative-AI failure mode: **orphaned scaffolding and parallel ways of doing the same thing**. The system carries a complete, fully-wired-into-the-schema "shadow" pipeline that nothing calls, a SignalR group mechanism that nothing uses, and three interchangeable copies of the auth helper. None of it breaks the build; all of it is dead weight a human reviewer would have deleted.

For the stated bar — secure, scalable, performant, but simple — it scores well on *secure* and *simple*, partially on *performant*, and has one real *scalability* red flag (the realtime fan-out).

| Dimension | Rating | Notes |
|---|---|---|
| Architecture clarity | ★★★★★ | Clean, documented, honest about deferrals |
| Security | ★★★★☆ | Strong basics; a few real gaps (below) |
| Scalability | ★★★☆☆ | Good data ownership; realtime fan-out is the weak point |
| Performance | ★★★☆☆ | Correct but N+1 DB patterns in hot paths |
| Maintainability | ★★★☆☆ | Dragged down by dead code & duplication |
| Test coverage | ★★★☆☆ | Meaningful behavioral tests, but thin breadth |

---

## 2. Architecture & Features

### Shape
- **1 Gateway/BFF + 8 domain services** (Identity, Tenant, Asset, TagCatalog, Edge, Telemetry, Alarm, Reporting) on .NET 10, plus a React/Vite SPA.
- **Database-per-service** (8 logical Postgres DBs, single container in dev). Strict rule: a service only touches its own DB; cross-service reads go over HTTP `/internal/v1/...`.
- **Two transport planes:** HTTP for synchronous request/response queries; **Wolverine** over **MQTT (Mosquitto) + PostgreSQL** for async commands/events, with durable inbox/outbox, tiered retry (1s/5s/30s), and a move-to-error-queue policy.
- **Lightweight Clean Architecture** per service: `Domain/` → `Application/` → `Infrastructure/` → `Program.cs` (minimal API wiring). Gateway and Edge are intentionally thinner.
- **Realtime:** Gateway owns a SignalR hub (`/hubs/telemetry`); MQTT events are bridged to the browser.

### Features
- Local auth (PBKDF2, JWT, 4 fixed roles), multi-tenant scoping with cross-tenant "SupportEngineer" visibility.
- Versioned Node-RED/MQTT telemetry contract (`alpha/{tenant}/{site}/{unit}/telemetry`, `schemaVersion 1.0`), normalized by the Telemetry service against a tag catalog.
- Current values + partitioned history + monthly report aggregates.
- Alarm lifecycle (raise/ack/clear) including communication-loss alarms driven by a stale-unit monitor.
- Optional CHP simulator (Edge), Docker Compose for dev, k3s manifests for prod-like, Prometheus/Grafana ops profile, Mosquitto ACLs + per-service credentials.

The architecture is appropriate and proportionate. Nothing is over-engineered for what it does, and the deferral lists (Sparkplug B, TimescaleDB, HA, OIDC, multi-region) are explicit and honest.

---

## 3. Strengths (what is done right)

1. **SQL is uniformly parameterized.** Every query across every repository uses `NpgsqlParameter`/`AddWithValue`. The only string-interpolated SQL fragments are *table names* drawn from hard-coded internal constants (`telemetry_samples`, `alarm_events`, …), never user input. No SQL-injection surface found in app code.
2. **Tenant isolation is consistent and centralized.** Almost every read carries `where (@is_support or tenant_id = @tenant_id)` with `RoleRules.IsSupport(user.Role)`. This pattern is applied uniformly, not ad hoc.
3. **Password hashing is correct** — PBKDF2-SHA256, 100k iterations, 16-byte salt, 32-byte key, versioned hash string, and `CryptographicOperations.FixedTimeEquals` for comparison.
4. **Messaging is production-shaped**, not toy: durable inbox/outbox, auto transactions, retry-with-cooldown then DLQ, per-service MQTT client IDs, JSON serialization aligned to web defaults.
5. **Defense in depth on auth:** internal services re-validate the JWT and enforce roles themselves (e.g. `RoleRules.CanAcknowledge` → 403 in the Alarm service), rather than trusting the Gateway.
6. **Secrets are externalized** (`.env.example`, `JWT_SECRET` required-or-throw at startup, per-service Mosquitto creds, ACL file). No secrets committed; `node_modules`/`dist` are correctly untracked.
7. **Honest, high-quality docs** (`system-overview.md`, ADR-002, messaging runbook) with mermaid flows that actually match the code.
8. **Tests target real behavior** — alarm broadcast, communication-loss alarms, primary ingestion, messaging bootstrap, Mosquitto security — not just trivial getters.

---

## 4. Warning Signs of AI Slop (code-quality)

These are the issues most characteristic of machine-generated code. None are catastrophic; collectively they're the main maintainability tax.

### 4.1 Dead "shadow" pipeline (highest-value cleanup)
A complete parallel "shadow" ingestion/evaluation pipeline exists but has **zero callers** in application code:
- `TelemetryRepository.IngestShadowAsync` → writes to `telemetry_samples_shadow` / `tag_current_shadow`
- `AlarmRepository.EvaluateShadowAsync` → `alarm_events_shadow`
- `AssetRepository.MarkShadowSeenAsync` → `unit_last_seen_shadow`
- `Topics.ShadowTelemetryStored` / `ShadowTelemetryStoredWildcard`
- Shadow tables (including a **partitioned** `telemetry_samples_shadow` with indexes) are still **created on every startup** by the migrators.

This is leftover scaffolding from the HTTP→MQTT migration (the "add shadow pipeline" → "promote MQTT to primary" → "remove legacy HTTP fan-out" sequence). It is now pure dead code *and* dead schema that consumes DDL/startup time and confuses readers. **Recommendation:** delete the shadow methods, topics, migrator blocks, and `ops/scripts/compare-shadow.sql` once the migration is confirmed complete.

### 4.2 SignalR group machinery is dead; broadcasts fan out to everyone
`TelemetryHub.JoinTenant` / `JoinUnit` build `tenant:{id}` / `unit:{id}` groups — but **nothing ever calls them** (the frontend `useSignalR` never invokes them), and **all four broadcast handlers use `hub.Clients.All`** (`TelemetryBroadcastHandler`, `AlarmBroadcastHandler`, `UnitStatusBroadcastHandler`, `ReportCompletedHandler`).

Consequences:
- **Dead code:** the group methods serve no purpose as written.
- **Cross-tenant metadata exposure:** every authenticated client (any tenant) receives `{tenantId, unitId, status, …}` for *every* tenant's events. The payloads are IDs/metadata only — the client then re-fetches via tenant-scoped REST — so it is **not a bulk telemetry leak**, but it does leak the existence, IDs, and activity timing of other tenants' units. For a multi-tenant SCADA platform that is a real isolation gap.
- **Scalability red flag (see §6):** the fan-out + blind re-fetch is a thundering-herd pattern.

**Recommendation:** broadcast to `tenant:{tenantId}` groups, and have the hub auto-join the caller's tenant group from JWT claims on connect (don't accept arbitrary tenant IDs from the client — see §5.2).

### 4.3 Triplicated auth helper
Three functions do the same "parse Bearer header → validate → CurrentUserDto":
- `GatewayAuth.Authenticate` (Gateway)
- `HttpUserContext.FromBearerToken` (ServiceDefaults) — used by domain services
- `MinimalApi.RequireUser` (ServiceDefaults) — the cleanest of the three, and **unused by the Gateway**

`GatewayAuth.Authenticate` is a near-verbatim copy of `HttpUserContext.FromBearerToken`. Consolidate to one shared helper.

### 4.4 Full ASP.NET auth pipeline configured, then bypassed
The Gateway registers `AddAuthentication().AddJwtBearer(...)` + `AddAuthorization()` and the middleware (`UseAuthentication/UseAuthorization`), but **none of the REST endpoints use `[Authorize]` / `.RequireAuthorization()`** — they each manually call `GatewayAuth.Authenticate(...)`. The configured JwtBearer pipeline is effectively only exercised by the `[Authorize]` SignalR hub. This is two parallel auth systems where one would do, and the manual path is repeated as boilerplate in ~13 endpoints.

### 4.5 Minor documentation drift
`README.md` and `system-overview.md` list **"Outbox/event bus"** under deferred/known limitations, but a durable Postgres outbox **is** implemented and active in `AlphaMessaging`. Small, but the kind of inconsistency that erodes trust in the docs.

---

## 5. Security Findings

| # | Severity | Finding |
|---|---|---|
| 5.1 | Medium | **Cross-tenant metadata over SignalR** (`Clients.All`, §4.2). Other tenants' unit IDs and event timing are visible to any authenticated user. |
| 5.2 | Medium | **Hub trusts client-supplied IDs.** `JoinTenant(tenantId)`/`JoinUnit(unitId)` add the connection to any group the client names, with no check against the caller's JWT tenant. Even though unused today, if wired up as-is it enables deliberate cross-tenant subscription. Derive the group from claims. |
| 5.3 | Low/Medium | **No token revocation; logout is a no-op server-side.** JWTs live 12h with no refresh/blacklist; `/api/auth/logout` only writes an audit row. A leaked token is valid until expiry. |
| 5.4 | Low | **JWT issuer/audience validation disabled** (`ValidateIssuer=false`, `ValidateAudience=false`). Acceptable for a single-issuer system but worth enabling before multi-service/OIDC expansion. |
| 5.5 | Low | **`/internal/v1/alarms/count` has no auth check at all** (unlike its sibling endpoints), relying entirely on network isolation. Inconsistent; add the bearer/role gate. |
| 5.6 | Low | **Token stored in `localStorage`** (frontend `tokenKey`) — standard XSS-exposure tradeoff; acceptable for the current threat model but note it. |
| 5.7 | Info | Gateway forwards the original bearer token to internal services *and* relies on them to enforce roles (good defense-in-depth), but the Gateway itself does no role checks — privileged routes are gated only downstream. |

No injection, no hardcoded secrets, no obviously broken crypto. The security posture is solid for a pilot; the items above are the gap to "enterprise."

---

## 6. Scalability & Performance

- **Realtime fan-out (the main concern).** Because every event goes to `Clients.All` and the client responds by re-fetching over REST, N connected operators × M tenant events produces O(N·M) refetch storms. This directly undercuts the "scalable/performant" goal and gets worse as tenants/operators grow. Group-scoped broadcasts (§4.2) largely fix it; pushing the changed payload instead of triggering a refetch would fix it fully.
- **N+1 database round trips in hot paths.** `TelemetryRepository.IngestIntoAsync` and `AlarmRepository.EvaluateIntoAsync` loop per-sample issuing one command each (ingest at least wraps a transaction; alarm eval does not). For high-frequency telemetry batches this is the first thing to bottleneck. Consider multi-row `INSERT ... unnest(@...)` / `COPY` for ingest and a set-based evaluation query.
- **Alarm raise is not atomic.** The `insert ... where not exists (... state in active/acknowledged)` dedup has no backing unique constraint, so two concurrent batches for the same tag could double-raise. Add a partial unique index (`unique (tag_id) where state in ('active','acknowledged')`) or rely on per-unit message ordering.
- **Single Postgres / single Mosquitto / 1 replica** — explicitly deferred (HA, clustering), which is reasonable for the stated pilot scope. The data-ownership boundaries mean horizontal scaling later is feasible.
- **CatalogCache** (1-min in-memory cache of tenant/unit/tag resolution) is a good touch that keeps ingestion off the network on the hot path.

---

## 7. Recommendations (prioritized)

**High value, low risk:**
1. Delete the entire dead **shadow** pipeline (code + topics + migrator DDL + `compare-shadow.sql`). (§4.1)
2. Switch broadcast handlers to **tenant-scoped groups**; auto-join the caller's tenant group from JWT on connect; stop accepting client-supplied tenant/unit IDs. (§4.2, §5.1, §5.2, §6)
3. Collapse the **three auth helpers** into one and use it everywhere. (§4.3)

**Medium:**
4. Either adopt `[Authorize]`/`.RequireAuthorization()` and drop the manual per-handler auth, or remove the unused JwtBearer pipeline — pick one model. (§4.4)
5. Add the missing auth gate on `/internal/v1/alarms/count`. (§5.5)
6. Add the partial unique index for alarm dedup. (§6)
7. Batch the per-sample ingest/eval into set-based SQL. (§6)

**Lower / future:**
8. Token revocation or short-lived access + refresh; meaningful server-side logout. (§5.3)
9. Fix the "Outbox deferred" doc drift. (§4.5)
10. Broaden tests: gateway auth/forwarding, tenant-isolation regression tests (would have caught §4.2), ingest idempotency under concurrency.

---

## 8. Bottom Line

The code is clean, secure in its fundamentals, and faithfully implements a sensible microservice SCADA architecture — better than most AI-generated projects of this size. The "slop" here is **accumulated scaffolding, not sloppiness**: a dead shadow pipeline, dead SignalR groups, and duplicated auth helpers that a human would have pruned. Fix the realtime fan-out (which is simultaneously the top scalability *and* the top tenant-isolation issue), delete the dead code, and unify the auth path, and this moves from "good pilot" to genuinely defensible as an enterprise foundation — while staying simple.
