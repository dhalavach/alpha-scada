# Task 15 — Downsample history reads; cap payloads

Read `README.md` in this folder first for repo context.

## Goal

`GET /telemetry/tags/{tagId}/history` returns at most ~2,000 points regardless of window, using the existing `telemetry_minute` continuous aggregate for long windows. The trend chart stays visually equivalent.

## Problem

`TelemetryRepository.GetHistoryAsync` (`src/Alpha.Scada.Telemetry/Infrastructure/TelemetryRepository.cs:111–134`) returns **every raw sample** in the window with no LIMIT. At 1 Hz the API-permitted 24h window = 86,400 rows serialized per request, then rendered as an 86,400-point SVG polyline in `TrendChart.tsx`. The `telemetry_minute` continuous aggregate (created in `TelemetryMigrator.SeedAsync`, `materialized_only = false` so recent data is merged in real time) already exists but is only used by report aggregation.

## Implementation steps

1. **Branch by window** in `GetHistoryAsync`:
   - `window <= 2h` → existing raw query, plus a safety `limit 10000`.
   - `window > 2h` → query the aggregate:
     ```sql
     select minute_utc, value_avg
     from telemetry_minute
     where tag_id = @tag_id and minute_utc >= @cutoff
     order by minute_utc
     limit 10000
     ```
     Map to `TelemetryHistoryPointDto(minute_utc, value_avg, "aggregated")`.
2. **Tenant scoping on the aggregate path.** `telemetry_minute` has no `tenant_id` column — do **not** rebuild the cagg. Guard with a cheap ownership check against `tag_current` (PK lookup, has `tenant_id`):
   ```sql
   where tag_id = @tag_id
     and (@is_support or exists (
           select 1 from tag_current tc
           where tc.tag_id = @tag_id and tc.tenant_id = @tenant_id))
   ```
   Edge case: a tag with history but no `tag_current` row yields empty results for non-support users — acceptable (no current row means it never ingested through the normalized path). Note it in a comment.
3. **Quality field**: the DTO keeps its shape; aggregated points carry the literal quality `"aggregated"`. Frontend: `TrendsScreen.tsx` renders quality badges with `tone={quality === "good" ? "good" : "warn"}` — add `"aggregated"` → neutral handling (extend the ternary or the `Badge` tone mapping) so a long-window chart isn't a wall of warning badges. Update the `HistoryPoint` quality union if Task 17's union types landed.
4. **Window cap stays 1440 min** (clamped in `Program.cs`); 240-min UI option now flows through the aggregate path. Optionally add a `1440` (24h) button to `TrendsScreen` now that it's cheap — single-line change, include it.

## Tests

- Repository-level (pattern: `BackendRepositoryTests` / `ReportOntologyConfigTests`, Testcontainers Timescale): seed >2h of minute-spaced samples, call with a 4h window → results come from the aggregate (quality `"aggregated"`), ordered, ≤ expected count; 30-min window → raw samples, quality preserved.
- Tenant isolation on the aggregate path: user from another tenant gets empty; support role gets data (mirror the existing isolation assertions).
- Cap: seed > 10k rows in-window (script-generated) → exactly 10000 returned. If seeding 10k rows is slow, drop to a smaller injected limit via a test-visible constant — keep the production constant at 10000.

## Constraints

- No new endpoint, no API shape change (`TelemetryHistoryPointDto` unchanged).
- Do not modify the continuous aggregate definition or refresh policy.
- The real-time merge (`materialized_only = false`) is what makes the ≤24h windows accurate despite the 3-day refresh window — don't "optimize" it away.

## Verification

```bash
dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
docker compose up --build   # let the simulator run a while
# 240m window in Trends renders quickly; payload size check:
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:5202/api/tags/<tagId>/history?minutes=240" | wc -c   # well under 200KB
```
