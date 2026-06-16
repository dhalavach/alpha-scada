# Alpha SCADA — Architecture & Code Review (Ranked Improvement Backlog)

**Date:** 2026-06-10
**Reviewer:** automated architecture review
**Scope:** Full tree at `main` (c43fb48): 9 services + ServiceDefaults (~8.8k LOC C#), React 19 frontend, compose/k3s/NATS/Timescale ops, 92 tests.
**Format:** Each task is written to be handed to a coding agent (Codex) as-is: problem → exact locations → change → acceptance criteria.

Verification status at time of review: `dotnet build` green, 0 warnings. `dotnet test` is **flaky**: 25/92 failed on a cold parallel run, 1/92 on a warm run, every failing test passes in isolation (see Task 10).

---

## Verdict

The codebase is in much better shape than the 2026-05-29 review: the shadow pipeline is gone, SignalR is tenant-group-scoped, ingestion is batched (`unnest`), alarm dedup has a partial unique index, and the NATS/JetStream ingestion path (adapter → pump → handler) is genuinely well-engineered, with bounded parallelism and explicit ack/nak/term outcomes.

The remaining problems cluster in four areas:

1. **Service-to-service trust is unfinished.** A whole class of `/internal/v1` endpoints is anonymous because services have no machine identity — and the workaround has been to leave auth off whichever endpoints services call.
2. **Two half-finished reliability mechanisms**: a DLQ that publishes into the void, and a hand-rolled alarm outbox layered on top of Wolverine's outbox.
3. **A domain correctness bug**: communication-loss alarms are never cleared.
4. **Vibecoding residue**: dead endpoints/methods/tables, alert rules for metrics that are never emitted, a fabricated availability KPI, the gateway squatting in the reporting database, demo seed data baked into production migrations.

---

# P0 — Security & correctness (do these first)

## Task 1 — Introduce service-to-service auth; remove all anonymous internal endpoints

**Problem.** User-facing internal endpoints require JWT, but every endpoint called service-to-service is mapped **outside** the `RequireAuthorization()` group, because callers (Reporting, Telemetry ingestion, Alarm) have no credentials. Anyone with network reach can hit:

| Service | Anonymous endpoint | File |
|---|---|---|
| Telemetry | `POST /internal/v1/telemetry/units/{unitId}/report-aggregate` | `src/Alpha.Scada.Telemetry/Program.cs:49` |
| Alarm | `GET /internal/v1/alarms/count` | `src/Alpha.Scada.Alarm/Program.cs:51` |
| Tenant | `GET /internal/v1/tenants/resolve/{key}`, `GET /internal/v1/tenants/{id}` | `src/Alpha.Scada.Tenant/Program.cs:24,30` |
| TagCatalog | `POST /internal/v1/tags/resolve`, `GET /internal/v1/report-config/units/{id}` | `src/Alpha.Scada.TagCatalog/Program.cs:25,28` |
| Asset | `GET /internal/v1/units/resolve`, `GET /internal/v1/units/{id}/route`, `GET /internal/v1/units/stale` | `src/Alpha.Scada.Asset/Program.cs:40–53` |

These leak tenant existence/keys, unit topology, alarm counts, and monthly production aggregates across tenants, and `report-aggregate` lets a caller run arbitrary aggregation queries.

**Change.**
1. Add a `ServiceTokenProvider` in `Alpha.Scada.ServiceDefaults` that mints a short-lived (5–10 min, cached) JWT via the existing `JwtTokenService` with a dedicated claim, e.g. `role = "Service"` and a synthetic tenant claim, plus `Roles.Service` constant and `RoleRules.IsService(...)`.
2. Add a named `DelegatingHandler` (registered in `AddAlphaServiceClients`) that attaches this token to outgoing requests **when no user Authorization header was forwarded** (keep the existing user-token forwarding for user-context calls).
3. Move every endpoint above into the `RequireAuthorization()` group. Endpoints that must be service-only (e.g. `tags/resolve`, `report-aggregate`, `units/resolve`, `units/{id}/route`, `alarms/count`, `report-config`) additionally check `RoleRules.IsService(user.Role)` (or a policy).
4. Delete `GET /internal/v1/units/stale` instead of securing it — it has zero callers (see Task 19).

**Acceptance criteria.**
- `grep -n "app.MapGet\|app.MapPost" src/*/Program.cs` shows no endpoint mapped outside an authorized group (operational endpoints `/health|/ready|/metrics` excepted).
- Calling any internal endpoint without a token returns 401; with a user token but service-only policy returns 403.
- Report generation (queued via NATS) still completes end-to-end (`ReportRequested` → `ReportCompleted`), proving the service token flows through Reporting → TagCatalog/Telemetry/Alarm.
- New integration test: anonymous request to `tenants/resolve/{key}` and `report-aggregate` is rejected.

## Task 2 — Clear communication-loss alarms when a unit comes back online

**Problem.** Comm-loss alarms (`tag_id is null`) are raised on `UnitStatusChanged(status=offline)` (`src/Alpha.Scada.Alarm/Application/UnitStatusAlarmHandler.cs:10` returns early on anything except `"offline"`), but **no code path ever clears them**: telemetry-driven clearing matches `tag_id = any(@tag_ids)` which never matches NULL (`src/Alpha.Scada.Alarm/Infrastructure/AlarmRepository.cs:84–93`). Once a unit recovers, the critical "communication lost" alarm stays active/acknowledged forever (and because the raise guard checks for any open comm alarm, the *next* real outage raises nothing).

**Change.**
1. In `UnitStatusAlarmHandler`, on `status == "online"` call a new `AlarmService.ClearCommunicationLostAsync(unitId, route, ct)`.
2. New repository method: `update alarm_events set state='cleared', cleared_at_utc=now() where unit_id=@unit_id and tag_id is null and state in ('active','acknowledged') returning ...`, enqueue `AlarmCleared` rows in the same transaction via the existing outbox enqueue.
3. Mirror the existing transition semantics (only emit events for rows actually updated).

**Acceptance criteria.**
- Integration test (extend `tests/Alpha.Scada.Tests/CommunicationLossAlarmTests.cs`): publish `offline` → alarm raised; publish `online` → `AlarmCleared` observed on `alpha.alarm.cleared` and `alarm_events.state='cleared'`.
- A second offline → online cycle raises and clears a second alarm.

## Task 3 — Make the telemetry dead-letter queue real (it currently drops into the void)

**Problem.** `TelemetryAdapterIngestionWorker.DeadLetterAsync` publishes to `alpha._dlq.telemetry.{subject}` over **core NATS** (`src/Alpha.Scada.Telemetry/Application/TelemetryAdapterIngestionWorker.cs:197`). No stream binds `alpha._dlq.>` (`AlphaMessaging.ConfigureNats` defines only EDGE/DOMAIN/JOBS), and nothing subscribes — so every dead-lettered envelope is lost the instant it's published. `docs/architecture-review.md:213` already documents this as a known gap. Additionally the DLQ record contains only subject/error/timestamps — **not the payload** — so even a captured record would be unreplayable, and messages that exhaust `MaxDeliver=5` (persistent non-poison failures) vanish without any record.

**Change.**
1. In `AlphaMessaging.ConfigureNats` (`src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessaging.cs:104`), define a fourth stream `ALPHA_DLQ` capturing `alpha._dlq.>` (long retention, e.g. 30 days; add `Topics.DlqStream` + `Topics.DlqWildcard` to `Topics.cs`).
2. Publish dead letters through JetStream (`jetStream.PublishAsync`) instead of `connection.PublishAsync` so delivery is confirmed.
3. Extend `DeadLetteredTelemetry` with `string PayloadBase64` (cap at ~64 KB) so messages can be inspected/replayed.
4. Add a max-delivery watchdog: subscribe to `$JS.EVENT.ADVISORY.CONSUMER.MAX_DELIVERIES.ALPHA_EDGE.telemetry-edge-json` and republish the referenced message to the DLQ subject (or at minimum, log + increment a metric).
5. Document the replay procedure in `docs/messaging-runbook.md`.

**Acceptance criteria.**
- Publishing a malformed JSON envelope to `alpha.t.s.u.telemetry` results in a persisted message on `ALPHA_DLQ` containing the original payload (integration test via Testcontainers NATS).
- `nats stream ls` in the runbook shows `ALPHA_DLQ`.
- Ingestion metrics still count the outcome as `dead_letter`.

## Task 4 — Lock down NATS: per-user permissions, externalized passwords

**Problem.** `ops/nats/nats-server.conf:16–22` ships three users with hardcoded passwords (`edge-pass`, `services-pass`, `admin-pass`) — committed to git, mirrored in `.env.example`, `ops/k3s/config.yaml` (a `Secret` with `stringData` in git), and **all 9 `appsettings.json`** files. There are **no authorization rules**: the `edge` user (shared by all field devices) can subscribe to every tenant's telemetry, publish forged domain events (`alpha.alarm.raised`, `alpha.status.changed`), and read the JOBS stream. For a multi-tenant SCADA broker this is the single biggest isolation gap.

**Change.**
1. Add `permissions` blocks: `edge` → publish only `alpha.*.*.*.telemetry` and `alpha.*.*.*.status`, no subscribe (or only its own command subjects); `services` → publish/subscribe `alpha.>` + JetStream API subjects (`$JS.API.>`, `$JS.ACK.>`, deliver subjects); `admin` → unrestricted.
2. Stop committing real passwords: generate `nats-server.conf` from a template at container start (NATS supports `$VAR` resolution in conf via `nats-server --config` with env preprocessing, or mount a generated conf from `ops/scripts/dev-setup.sh`), and strip the password values from `appsettings.json` (keep only `Nats.Url`; user/password come from env). Remove credentials from `ops/k3s/config.yaml` in favor of a documented `kubectl create secret` step or SOPS.
3. Longer term (note in docs, not required now): per-site edge credentials or NATS accounts for tenant-level isolation.

**Acceptance criteria.**
- `tests/Alpha.Scada.Tests/NatsSecurityTests.cs` extended: edge credentials can publish telemetry but get permission-denied subscribing to `alpha.alarm.>` and publishing `alpha.alarm.raised`.
- `grep -rn "services-pass\|edge-pass" src ops` returns only the dev-setup generator/template (ideally nothing).
- Full compose stack still boots and ingests simulator telemetry.

---

# P1 — High-value architecture & operability

## Task 5 — Give the Gateway its own database (it currently squats in Reporting's)

**Problem.** Gateway's connection string points at `alpha_reporting` in *all three* config sources (`src/Alpha.Scada.Gateway/appsettings.json`, `docker-compose.yml:12`, `ops/k3s/services.yaml`). Two different Wolverine applications (gateway + reporting) share one `wolverine` schema: their inbox/outbox/dead-letter tables and node-leadership records interleave, the gateway's `/metrics` error-queue gauge reports Reporting's dead letters and vice versa, and DB-per-service is silently violated.

**Change.** Add `alpha_gateway` to `ops/postgres/init.sql`, point gateway at it in `appsettings.json`, `docker-compose.yml`, and `ops/k3s/services.yaml`. Note in the runbook that existing dev volumes keep stale gateway state in `alpha_reporting`'s wolverine schema (recreate volume or ignore).

**Acceptance criteria.** Compose stack boots; gateway `/ready` passes; `psql -d alpha_gateway -c "\dt wolverine.*"` shows gateway envelope tables; reporting unaffected; full report round-trip works.

## Task 6 — Stop durably inboxing the realtime broadcast path

**Problem.** `UseDurableInboxOnAllListeners()` (`src/Alpha.Scada.ServiceDefaults/Messaging/AlphaMessaging.cs:65`) applies to the gateway's listeners, whose handlers only fan out to SignalR (`TelemetryBroadcastHandler` etc.). Every 1 Hz `TelemetryBatchStored` is therefore written to the gateway's Postgres inbox before being pushed to browsers (~86k rows/day/unit of pure overhead), and when the gateway restarts after downtime, JetStream + durable inbox **replay the backlog as stale UI pushes**. Realtime fan-out wants at-most-once/latest, not durable at-least-once.

**Change.**
1. Add `ListenForEphemeralDomainEvent(subject)` to `AlphaMessagingTopology` that configures the NATS listener **non-durable** (no durable consumer name → ephemeral JetStream consumer or plain NATS subscription) and `.BufferedInMemory()` (Wolverine inline/buffered, not durable).
2. Use it in `src/Alpha.Scada.Gateway/MessagingTopology.cs` for `telemetry-stored`, `status`, and the three alarm events. Keep `ReportCompleted` durable if you want completion toasts to survive restarts; keep the JOBS publish durable (it is the outbox side).
3. Verify `AutoApplyTransactions` no longer opens a Postgres transaction per broadcast on these endpoints.

**Acceptance criteria.**
- With the stack running, `select count(*) from wolverine.wolverine_incoming_envelopes` in the gateway DB stays flat while telemetry flows.
- Restarting the gateway after 5 minutes of downtime does **not** replay old telemetry pushes (UI receives only new samples).
- `tests/Alpha.Scada.Tests/RealtimeTenantIsolationTests.cs` and `AlarmBroadcastTests.cs` still pass.

## Task 7 — Tighten the JWT trust model

**Problem.** One symmetric secret signs everything and is distributed to **all nine services** (`Jwt__Secret` in every compose/k3s env) — any compromised service can mint admin tokens for any tenant. `ValidateIssuer=false, ValidateAudience=false` (`src/Alpha.Scada.ServiceDefaults/JwtTokenService.cs:34–35`). Tokens live 12h (`AuthService.cs:19`) with no refresh or revocation; logout is an audit row; the SPA keeps the token in `localStorage`.

**Change (incremental, in order of value):**
1. Switch issuance to **RS256/ES256**: Identity holds the private key (env/secret); all other services get only the public key for validation. `JwtTokenService` splits into issuer (Identity-only) and validator (ServiceDefaults). This is contained — token creation already lives in one class.
2. Set and validate `iss=alpha-scada-identity`, `aud=alpha-scada`.
3. Reduce access-token lifetime to 60 min and add `POST /internal/v1/auth/refresh` (rotating refresh token, httpOnly cookie via gateway, server-side stored hash, revoked on logout). Frontend: refresh on 401-once-retry.
4. Frontend stores the access token in memory; only the refresh cookie persists.

**Acceptance criteria.** Login/refresh/logout round-trip works in the UI; a token minted with the old shared secret is rejected; services boot without the signing key; `ServiceDefaultsSecurityTests` extended for issuer/audience rejection.

## Task 8 — Brute-force & DoS protection on login

**Problem.** `POST /api/auth/login` (gateway → identity) has no rate limit, no lockout, no captcha/backoff. Each attempt costs a full PBKDF2 verification (100k iterations) — an unauthenticated CPU-burn amplifier and credential-stuffing surface. 100k iterations is also below the OWASP 2023 recommendation (600k for PBKDF2-HMAC-SHA256).

**Change.**
1. Gateway: `AddRateLimiter` with a fixed-window policy per client IP on the login route (e.g. 10/min, queue 0) + a modest global limit on `/api/*`.
2. Identity: per-account failure tracking (e.g. `failed_logins` + `locked_until_utc` columns) with exponential backoff after N failures; audit lockouts.
3. Bump `PasswordHasher.Iterations` to 600_000 for new hashes and transparently re-hash on successful login when the stored iteration count is lower (the versioned format already carries iterations).

**Acceptance criteria.** 11th login attempt within a minute from one IP returns 429; 10 consecutive wrong passwords lock the account (subsequent correct password also rejected until expiry, with distinct audit event); existing demo-user logins still succeed and get silently upgraded to 600k iterations.

## Task 9 — Replace hand-rolled observability with OpenTelemetry; fix broken alerts

**Problem.**
- No tracing at all; `ServiceIdentity` builds correlation IDs from `Activity.Current` but nothing creates activities or exports them.
- `/metrics` is a hand-built string (`src/Alpha.Scada.ServiceDefaults/MinimalApi.cs`), and **every scrape runs `select count(*) from wolverine.wolverine_dead_letters`** — a full table scan per service per 15s scrape interval, plus a second connection for the Timescale row estimate.
- `alpha_scada_telemetry_samples_written_total` is declared `gauge` but suffixed `_total` (Prometheus naming violation; it's also an approximate row count, not a written-samples counter).
- `ops/prometheus/alerts.yml:5` alerts on `alpha_scada_wolverine_outbox_depth`, a metric **no code emits** — the outbox-backlog alert can never fire (the NATS rules at least admit they're inert; this one doesn't).

**Change.**
1. Add `OpenTelemetry.Extensions.Hosting` + ASP.NET Core/HttpClient/Npgsql instrumentation + `OpenTelemetry.Exporter.Prometheus.AspNetCore` to ServiceDefaults; expose `/metrics` from the OTel exporter. Port `TelemetryIngestionMetrics` to `System.Diagnostics.Metrics` (Counter/Histogram/UpDownCounter) — delete the two duplicated `EscapeLabel` implementations and the hand-built exposition.
2. Emit real `alpha_scada_wolverine_outbox_depth` and `..._error_queue_depth` from a single cheap background sampler (one query every 15–30s, cached; not per scrape).
3. Rename/fix the gauge-vs-counter mismatch; update `ops/grafana/dashboards/*.json` and `ops/prometheus/alerts.yml` to the new names; delete or implement the inert NATS alert rules (NATS 8222 + `prometheus-nats-exporter` is the cheap path).
4. Wire OTLP trace export behind config (off by default).

**Acceptance criteria.** `/metrics` on every service serves OTel-generated exposition including ingestion histograms; scraping does not open new Postgres connections per request (verify via `pg_stat_activity` while hammering `/metrics`); every alert expression in `alerts.yml` matches an actually-emitted metric name; Grafana dashboards render.

## Task 10 — Fix test-suite flakiness (parallel Testcontainers stampede)

**Problem.** Cold run: 25/92 fail in ~0.4s total (instant Docker/daemon contention — image pulls and parallel container startup); warm run: 1 fails; isolated: all pass. The suite cannot gate CI in this state. Each integration test class builds its own Postgres (+ NATS) container inline and xUnit runs collections in parallel.

**Change.**
1. Introduce shared collection fixtures: one Postgres container and one NATS container per test run (`ICollectionFixture`), with per-test databases/streams (databases are already cheap: `create database` per test or schema-per-test).
2. Mark container-based test classes `[Collection("containers")]` so they serialize against the shared fixture, or set `parallelizeTestCollections: false` in an `xunit.runner.json` if simpler.
3. Make "Docker unavailable" deterministically `Skip` (the existing `DockerUnavailableException` → `SkipException` path only covers some classes — e.g. `ReportOntologyConfigTests` and `ServiceDefaultsEndpointTests` evidently fail instead).
4. Pre-pull images in CI before `dotnet test`.

**Acceptance criteria.** Three consecutive `dotnet test Alpha.Scada.slnx` runs from a cold Docker state pass 100% (or skip cleanly without Docker); total runtime not worse than ~2× the current warm run.

---

# P2 — Hardening, performance, domain quality

## Task 11 — Gateway correctness & hygiene batch

All in `src/Alpha.Scada.Gateway/Program.cs`:

1. **Parallelize the fan-in** (lines 93–94): the tag-catalog and telemetry calls are awaited sequentially; send both, then `Task.WhenAll`.
2. **Stop defaulting missing values to `0`** (line 113): `current?.Value ?? 0` renders a dead tag as a plausible reading of `0` — dangerous in a SCADA UI. Make `TagCurrentDto.Value` nullable (`double?`), propagate `null`, and render `--` in the UI (UI already handles quality "stale").
3. **Validate inputs at the boundary**: `minutes` → clamp/400 outside `[1, 1440]` (today the gateway forwards anything; only Telemetry clamps); `request.Period` → validate with `MonthPeriod.Parse` and return 400 (today a bad period becomes an `InvalidOperationException` deep in Reporting → 5 retries → error queue).
4. **Decide and enforce a role policy for `POST /api/reports/monthly/run`** — any Viewer can currently trigger report generation; `RoleRules.CanManageConfiguration` exists and is used nowhere (Task 19). Either gate report runs (Operator+) or delete the dead rule.
5. **Login proxy status fidelity** (lines 58–64): a downstream 5xx currently surfaces as 401 ("wrong password") — map non-2xx/non-401 to 502.
6. **Add `UseExceptionHandler` + ProblemDetails** to all services (one helper in ServiceDefaults) so unhandled exceptions return a consistent JSON shape instead of bare 500s.

**Acceptance criteria.** Unit/units-current endpoint latency drops to ~max(two calls) under load; `GET /api/tags/{id}/history?minutes=999999` → 400; `POST /api/reports/monthly/run` with `period: "2026-13"` → 400 and never reaches NATS; viewer role gets 403 on report run (if gating chosen); identity-service-down login attempt returns 502 not 401.

## Task 12 — Collapse the double outbox in the Alarm service (or finish the hand-rolled one)

**Problem.** Alarm events are persisted **twice** before reaching NATS: once in the custom `alarm_outbox` (`AlarmRepository.EnqueueOutboxAsync`), then the dispatcher (`AlarmOutboxDispatcher`) calls `bus.PublishAsync`, which lands them in **Wolverine's durable outbox** for a second persisted hop. Additional defects in the custom layer:
- The dispatcher publishes **inside an open transaction** (`DispatchPendingAsync` holds `for update skip locked` row locks across NATS I/O for up to `BatchSize` publishes).
- **Dispatched rows are never deleted** — `alarm_outbox` grows forever.
- Rows that exhaust `MaxAttempts` are silently abandoned (log only), invisible to metrics/alerts.

**Change (preferred: option A).**
- **A. Use Wolverine as the only outbox.** `TelemetryStoredAlarmHandler`/`UnitStatusAlarmHandler` are Wolverine handlers; with `AutoApplyTransactions` + cascading messages, returning the `AlarmRaised/Cleared/Acknowledged` events from the handler gives transactional outbox semantics for free. Refactor `AlarmService.EvaluateAsync` to return events to the handler instead of enqueueing; for the HTTP ack path, inject `IMessageBus` and use Wolverine's outbox transaction middleware (or `IDbContextOutbox`-style manual enlistment with the same Npgsql connection). Delete `AlarmOutboxDispatcher`, `AlarmOutboxEvents`, `IAlarmOutboxSignal`, the `alarm_outbox` table migration (add a drop migration), and `AlarmOutboxTests` (replace with cascading-message tests).
- **B. If the custom outbox stays** (e.g. you want publish-order control): commit the read transaction before publishing, mark dispatched in a follow-up statement, `delete from alarm_outbox where dispatched_at_utc < now() - interval '7 days'` in the sweep, and surface `attempts >= max` rows as a gauge.

**Acceptance criteria.** Exactly one persistence hop between `alarm_events` commit and NATS publish; alarm raise/clear/ack still arrive on their subjects exactly once under a NATS outage + recovery test (dedup header preserved — `DeduplicationId` currently set from outbox row id must survive the refactor, e.g. use the alarm event id); `alarm_outbox` table gone (A) or bounded (B).

## Task 13 — Get demo seed data out of production migrations

**Problem.** Demo tenants/sites/units/tags ship in **unconditional** migrations/seeds, unlike Identity which gates demo users behind `Seed:DemoUsers`/environment:
- `TenantMigrator` `001_initial` inserts `demo-operator`/`field-operator` *inside the versioned migration*.
- `AssetMigrator` `001_initial` inserts demo sites/units inside the migration.
- `TagCatalogMigrator.SeedAsync` seeds 15 demo tags + report profiles for hardcoded unit GUIDs unconditionally.

Every production deployment gets a fake tenant with predictable GUIDs (and Task 1's anonymous `tenants/resolve` would happily confirm it).

**Change.** Move all demo inserts out of versioned migrations into `SeedAsync` implementations gated on `Seed:DemoData` (default: on in Development, off otherwise — same pattern as `IdentityMigrator`). Keep `report_metric_definitions` seeding unconditional (that's reference data, not demo data). Set `Seed__DemoData=true` in docker-compose only.

**Acceptance criteria.** Fresh boot with `Seed__DemoData=false` creates schema + metric definitions but zero tenants/sites/units/tags; compose dev boot is unchanged; migration IDs unchanged for existing databases (the inserts being idempotent `on conflict do nothing` means removing them from `001_initial` is safe for already-migrated DBs — verify the migration row, not the SQL, is what's tracked).

## Task 14 — Downsample history reads; cap payloads

**Problem.** `GET /telemetry/tags/{tagId}/history` returns every raw sample in the window with no `LIMIT` (`src/Alpha.Scada.Telemetry/Infrastructure/TelemetryRepository.cs:111–134`). At 1 Hz, the 24h window the API allows = 86,400 rows serialized per request, and `TrendChart.tsx` renders them as one SVG polyline with 86k points. The `telemetry_minute` continuous aggregate already exists but is only used by reports.

**Change.** In `GetHistoryAsync`: windows ≤ 2h → raw samples; > 2h → query `telemetry_minute` (avg per bucket; include a quality roll-up or omit quality for aggregated points — extend the cagg if needed). Alternatively a single `time_bucket` query with bucket width chosen so the response is ≤ ~2,000 points. Add a hard `limit 10000` safety net either way.

**Acceptance criteria.** `minutes=1440` returns ≤ 2k points and < 200 KB; chart still renders; 30-minute window behavior unchanged; query plan for the aggregated path hits `telemetry_minute`.

## Task 15 — Negative caching + real dead-lettering for unresolvable telemetry

**Problem.** `CatalogCache.ResolveAsync` throws on unknown tenant/unit (`InvalidOperationException`), which the worker treats as transient: NAK → up to 5 redeliveries → silent disappearance (Task 3). Meanwhile each attempt costs up to 3 internal HTTP calls — a stream of bogus tenant keys (or one misconfigured edge device) hammers Tenant/Asset/TagCatalog. Unknown *tags* are silently dropped (acknowledged TODO at `CatalogCache.cs:21`).

**Change.**
1. Introduce `TelemetryResolutionException` (carrying which key failed); `CatalogCache` throws it for 404s and caches the *negative* result for 60s (cache entry "unknown") so repeats don't re-query.
2. Worker catches it alongside `InvalidTelemetryEnvelopeException` → DLQ (terminal), not NAK.
3. Count dropped-unknown-tag samples in `TelemetryIngestionMetrics` (new outcome label or counter) so a misconfigured tag list is visible.

**Acceptance criteria.** Publishing telemetry for a nonexistent tenant produces one resolution attempt + one DLQ entry within the first delivery (no 5× redelivery, ≤ 3 HTTP calls total in the negative-cache window); unknown-tag drops appear in `/metrics`.

## Task 16 — Containers: graceful shutdown, layer caching, non-root

**Problem.** `src/Dockerfile.service`:
- `ENTRYPOINT dotnet "$SERVICE_DLL"` is **shell form** — PID 1 is `/bin/sh`, SIGTERM never reaches dotnet, so every stop is a 10s-grace-then-SIGKILL: NATS consumers don't drain, Wolverine doesn't flush, in-flight telemetry processing is killed mid-transaction (compose `stop`, k8s rollouts).
- `COPY . .` before `dotnet restore` → any file change invalidates the restore layer; no `.csproj`-only restore stage.
- Runs as root; no `HEALTHCHECK`.

**Change.** Exec-form entrypoint via a tiny `entrypoint.sh` with `exec dotnet "$SERVICE_DLL"` (env var indirection is why shell form was used — `exec` fixes signal delivery while keeping the variable); copy `*.csproj`/`slnx` first, `dotnet restore`, then copy the rest; add `USER app` (the aspnet image ships one); optional `HEALTHCHECK CMD wget -qO- http://localhost:8080/health`. Also add `restart: unless-stopped` to compose services and upgrade `depends_on` to `service_healthy` where healthchecks exist.

**Acceptance criteria.** `docker compose stop telemetry` logs "Application is shutting down..." (host lifetime) instead of being killed; rebuilding after touching one `.cs` file reuses the restore layer; `docker inspect` shows non-root user.

## Task 17 — Don't hold DB transactions across HTTP calls in Asset

**Problem.** `AssetService.MarkStaleUnitsOfflineAsync` (`src/Alpha.Scada.Asset/Application/AssetService.cs:49–65`) opens a transaction, updates/locks all stale unit rows, then loops over `tenantKeyResolver.ResolveAsync` (HTTP to Tenant, 10s total timeout each) **before** committing. A slow/down Tenant service keeps unit rows locked for the duration, blocking `SetUnitOnlineAsync` (which takes `for update` on the same rows) — i.e. the comm-loss sweep can block recovery processing.

**Change.** Commit immediately after `repository.MarkStaleUnitsOfflineAsync`, then resolve tenant keys and build events outside the transaction (statuses are already final; event emission failure is retried by the worker loop anyway). Same review for `SetUnitOnlineAsync` route-resolution path (it resolves before opening the tx — fine).

**Acceptance criteria.** With Tenant service paused, the sweep still commits offline transitions promptly and `SetUnitOnlineAsync` is not blocked > ~100ms (manual or integration verification); events are emitted after Tenant recovers (or the sweep retries next tick).

## Task 18 — Make report "availability" honest (or label it)

**Problem.** `ReportingService.GenerateMonthlyAsync:52`: `availability = alarmCount > 0 ? profile.AvailabilityWithAlarmsPercent : profile.AvailabilityNoAlarmsPercent` — the headline availability KPI in the monthly report is a **configured constant** (99.5/98.5 seeded), not a measurement. A customer-facing report contains a fabricated number that telemetry data could actually support.

**Change (pick one, recommend 1):**
1. Compute availability = runtime-minutes ÷ minutes-in-period (data already available: the `runtime_hours` aggregate ÷ period hours), optionally subtracting comm-loss downtime from alarm history.
2. If the configured factors are a deliberate contractual fiction, rename fields to `contractual_availability_*` and label it in the UI/report as nominal.

Also delete `ReportingService.RunMonthlyAsync` (dead — no caller; the only entry point is the queued handler).

**Acceptance criteria.** Reported availability changes when a unit is offline for part of the period (test: seed minute aggregates for half the period → ~50%); `RunMonthlyAsync` gone; `ReportOntologyConfigTests` updated.

---

# P3 — Cleanup, frontend, repo hygiene

## Task 19 — Dead-code sweep (vibecoding residue)

Delete, with a quick grep-for-callers check on each:

| Item | Location | Evidence |
|---|---|---|
| `ReportingService.RunMonthlyAsync` | `src/Alpha.Scada.Reporting/Application/ReportingService.cs:12` | zero callers (covered in Task 18) |
| `RoleRules.CanManageConfiguration` | `src/Alpha.Scada.Contracts/Auth/AuthContracts.cs:23` | zero callers (or adopt in Task 11.4) |
| `GET /internal/v1/units/stale` + `AssetService/Repository.GetStaleUnitsAsync` | `src/Alpha.Scada.Asset/Program.cs:52`, `AssetService.cs:67`, `AssetRepository.cs:190` | zero callers |
| `edge_devices` table + `EdgeMigrator` (and Edge's Postgres dependency if nothing remains) | `src/Alpha.Scada.Edge/Infrastructure/EdgeMigrator.cs` | `credential_hash` etc. never read/written; Edge only simulates. Note: `/ready`+`/metrics` require `NpgsqlDataSource` — either keep a minimal DB or drop those endpoints for Edge |
| `EdgeStatusEnvelope` | `src/Alpha.Scada.Contracts/Edge/EdgeContracts.cs` | never produced/consumed |
| `Topics.EdgeMqttTelemetry/EdgeMqttStatus`, `Topics.StatusWildcard`, `Topics.AlarmWildcard`, `Topics.TelemetryStoredWildcard` | `src/Alpha.Scada.ServiceDefaults/Messaging/Topics.cs` | verify with grep; MQTT helpers are aspirational (MQTT listener is documented as reserved — keep only if a doc references them) |
| Duplicated SHA-256 message-id helper | `ChpUnitSimulatorWorker.DeterministicMessageId` vs `TelemetryAdapterIngestionWorker.ResolveMessageId` | extract one helper into ServiceDefaults.Messaging |
| Duplicated `EscapeLabel` | `MinimalApi.cs:69` + `TelemetryIngestionMetrics.cs:123` | dies anyway with Task 9 |
| `EdgeTelemetryEnvelope` vs `TelemetryEnvelopeV1` | `Contracts/Edge` vs `Contracts/Messaging` | two identical shapes for the same wire format; keep `TelemetryEnvelopeV1` (it has the `schemaVersion` JSON mapping) and have the simulator use it |

**Acceptance criteria.** Build green, tests green, `grep` finds no references to removed symbols, README/system-overview updated where they mention removed pieces.

## Task 20 — Frontend robustness batch

`src/Alpha.Scada.Web`:

1. **Expired-token handling**: `getJson` throws a generic `Error`; a 401 (12h token expiry mid-shift) leaves a broken dashboard. Centralize: on 401 → clear token → render Login. With Task 7.3, attempt one refresh first.
2. **`loadInitial` failures are unhandled** (`App.tsx:60–63` fires an un-awaited async without catch) → unhandled rejection + blank UI. Add error state + retry UI; add a React error boundary.
3. **Role-gate the UI**: Admin nav item and alarm Ack buttons render for Viewers (server rejects, UI shouldn't offer). `user.role` is already loaded.
4. **Move build tooling to `devDependencies`** (`vite`, `typescript`, `@vitejs/plugin-react` are in `dependencies`); add `eslint` + `typescript-eslint` + a `lint` script; add `"strict": true` check to CI (verify `tsconfig.json`).
5. **Unhardcode the process flow**: `processSteps` in `lib/format.ts` hardcodes five tag keys; derive the overview KPIs/process strip from tag metadata (e.g. subsystem ordering) or serve a per-unit layout from TagCatalog — otherwise every non-demo unit shows `--`.
6. **Drop the raw `/metrics` dump** from `AdminScreen` (and stop proxying `/metrics` publicly in `nginx.conf` + gateway; ops have Prometheus). Add basic security headers + gzip + static asset caching to `nginx.conf`.

**Acceptance criteria.** Expired token → clean redirect to login; killing the gateway mid-session shows an error state, not a white screen; viewer sees no Admin nav/Ack buttons; `npm run lint` exists and passes; `curl frontend/metrics` → 404.

## Task 21 — Repo & engineering hygiene

1. **CI pipeline** (none exists — no `.github/`): build + test (with Docker for Testcontainers, after Task 10) + frontend `tsc --noEmit`/build + `docker build`. Even a minimal workflow changes the review posture of everything above.
2. **Central package management**: add `Directory.Build.props` (`TargetFramework`, `Nullable`, `TreatWarningsAsErrors`, analyzers) and `Directory.Packages.props` — versions are currently pinned per-csproj (`10.0.0`, `5.21.0`, …) in 14 places.
3. **Untracked clutter at repo root**: a leading-space telemetry diagram, `build.log`, review/cleanup markdown, pre-rename screenshots, and tracked author-session filenames. Move review docs to `docs/reviews/`, screenshots to `docs/img/`, delete `build.log`, and use neutral document names.
4. **`.env.example` ships real default passwords** that match the committed NATS conf — after Task 4, regenerate so the example contains placeholders only.
5. **Case-insensitive email uniqueness**: `users.email` is `unique` but lookups are `lower(email)=lower(@email)` (`IdentityRepository.cs:15`) — `Admin@x` and `admin@x` can coexist as distinct accounts while login matches either nondeterministically. Add `create unique index ux_users_email_lower on users (lower(email))` migration (dedupe first) and normalize on insert. Also note: failed-login audit writes attacker-controlled email strings unbounded — cap length, and add a retention/cleanup job for `audit_events`.
6. **Migration runner nit**: `SqlDatabaseMigrator.MigrateAsync` creates `alpha_schema_migrations` *before* taking the advisory lock (`DatabaseMigrations.cs:30–38`) — two services bootstrapping the same fresh DB can race `create table if not exists` (harmless usually, noisy `pg_type` unique violations possible). Take the lock first (session-level `pg_advisory_lock` on a constant) or swallow the benign race. Low priority since DBs are per-service — but the gateway/reporting share makes it real until Task 5 lands.

---

## What's already good (don't churn)

- Parameterized SQL everywhere; consistent `(@is_support or tenant_id=@tenant_id)` tenant scoping; PBKDF2 with constant-time compare and versioned hash format.
- Telemetry write path: single round-trip `unnest` batch insert + `distinct on` current-value upsert with monotonic-timestamp guard; idempotent via `(tag_id, timestamp_utc)` PK + `Nats-Msg-Id` dedup.
- Timescale setup: hypertable, compression after 7d segmented by tag, configurable retention, continuous aggregate with refresh policy.
- Ingestion worker: explicit ack/nak/term outcome model, bounded `Parallel.ForEachAsync`, per-outcome metrics with histogram; transition-only status events; `for update skip locked` in the dispatcher; alarm dedup via partial unique index.
- Test suite shape (unit + Testcontainers integration incl. tenant-isolation and NATS auth tests) is right — it just needs the parallelism fix.

## Suggested execution order for Codex

Each task is one PR. Order: **1 → 2 → 3 → 4** (security/correctness), then **5 → 6** (small, unblock 9's metrics accuracy), **10** (so CI can gate the rest), **21.1** (CI), then 7, 8, 9, 11–18 in any order, P3 last. Tasks 1, 7 and 9 touch ServiceDefaults — land them separately to keep diffs reviewable.
