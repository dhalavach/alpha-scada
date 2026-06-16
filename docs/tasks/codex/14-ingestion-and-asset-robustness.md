# Task 14 — Ingestion resolution robustness + Asset transaction discipline

Read `README.md` in this folder first for repo context.

Two small, unrelated-in-code but same-theme fixes. **Two commits.**

## Part A — Unresolvable telemetry: negative caching + terminal dead-letter

### Problem

`CatalogCache.ResolveAsync` (`src/Alpha.Scada.Telemetry/Application/CatalogCache.cs`) throws `InvalidOperationException` when a tenant/unit is unknown. The ingestion worker treats that as transient: NAK → up to `MaxDeliver=5` redeliveries → message vanishes via the max-deliveries path (now at least counted, since Task 03). Each attempt costs up to 3 internal HTTP calls — one misconfigured edge device (or a stream of bogus tenant keys) hammers Tenant/Asset/TagCatalog ~15 HTTP calls per message. Unknown *tags* are silently dropped with only a debug-level trail (acknowledged TODO at `CatalogCache.cs:21`).

### Implementation

1. New exception `TelemetryResolutionException(string keyKind, string key)` in `src/Alpha.Scada.Telemetry/Application/Messaging/` (alongside `InvalidTelemetryEnvelopeException`).
2. `CatalogCache`: when Tenant/Asset returns 404/null for a tenant or unit, cache a **negative** marker for 60s (same `IMemoryCache`, e.g. store a sentinel under the same cache key) and throw `TelemetryResolutionException`. Repeats within the window throw from cache without HTTP. Successful resolutions overwrite the negative entry naturally on expiry. (`GetFromJsonAsync` throws `HttpRequestException` on 404 — catch it, check `StatusCode == NotFound`, then negative-cache; any other status stays transient.)
3. `TelemetryAdapterIngestionWorker.ProcessAsync`: catch `TelemetryResolutionException` alongside `InvalidTelemetryEnvelopeException` → `DeadLetterAsync` (terminal, with payload — infrastructure from Task 03). Not a NAK.
4. Unknown-tag visibility: in `CanonicalTelemetryHandler` (or `CatalogCache`), count dropped unknown-tag samples via a new method on `TelemetryIngestionMetrics` — `RecordUnknownTagsDropped(int count)` emitting counter `alpha_scada_telemetry_ingestion_unknown_tags_dropped_total` (follow the existing counter style). Keep the existing warning log for the all-samples-dropped case.

### Tests

- Negative-cache unit test: fake `IHttpClientFactory` (stub handler returning 404) — two resolves within the window produce **one** HTTP call and two `TelemetryResolutionException`s.
- Integration (pattern: `TelemetryPrimaryIngestionTests`): publish telemetry for a nonexistent tenant key → exactly one DLQ record appears (no 5× redelivery), with `ErrorType = nameof(TelemetryResolutionException)`.
- Metrics: unknown-tag drop increments the counter (direct unit test on the metrics class + handler).

## Part B — Asset: don't hold row locks across HTTP calls

### Problem

`AssetService.MarkStaleUnitsOfflineAsync` (`src/Alpha.Scada.Asset/Application/AssetService.cs:49–65`) opens a transaction, runs the UPDATE that locks every stale unit row, then loops over `tenantKeyResolver.ResolveAsync` — an HTTP call to Tenant with a 10s total timeout — **before** committing. A slow/down Tenant service keeps those rows locked, which blocks `SetUnitOnlineAsync` (`for update` on the same rows) — i.e., the stale-sweep can block recovery processing for the very units that just came back.

### Implementation

Commit immediately after `repository.MarkStaleUnitsOfflineAsync(connection, transaction, ...)` returns; resolve tenant keys and build the `UnitStatusChanged` events **after** the commit. Event-building failure after commit is acceptable: statuses are already final, the worker loop (`CommunicationLossMonitorWorker`) logs and continues, and the next sweep emits nothing extra (idempotent — already-offline units aren't re-selected). State this tradeoff in a short comment. Also pre-warm friendly: `TenantKeyResolver` has a 5-min cache, so the common case makes zero HTTP calls.

### Tests

Extend `tests/Alpha.Scada.Tests/AssetRepositoryBehaviorTests.cs` (or a focused new fact): with a deliberately hanging fake Tenant endpoint (delay > a few hundred ms), a concurrent `SetUnitOnlineAsync` on a just-marked-offline unit completes promptly (< ~1s) instead of waiting for the resolver — proving locks are released before HTTP.

## Constraints

- No changes to `UnitStatusChanged` content, subjects, or the comm-loss raise/clear logic (Tasks 02/07 territory).
- Negative-cache TTL 60s, hardcoded const next to the existing `CacheDuration` — no new config keys.

## Verification

```bash
dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
docker compose up --build
# Part A live check: publish a telemetry message with a bogus tenant key (edge creds, any unit), then:
#   - ALPHA_DLQ receives exactly one record for it
#   - telemetry /metrics shows dead_letter incremented once, and tenant service logs show no 404 storm
# Part B is covered by the test; optionally: docker compose pause tenant, wait for a sweep, confirm telemetry-driven
#   SetUnitOnline (unit back online in UI) is not delayed; unpause.
```
