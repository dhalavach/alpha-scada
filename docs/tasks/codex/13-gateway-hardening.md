# Task 13 — Gateway hardening batch

Read `README.md` in this folder first for repo context.

## Goal

The Gateway validates inputs at the boundary, parallelizes its one fan-in endpoint, never renders a dead sensor as a plausible `0`, gates report runs by role, reports honest status codes, and all services return consistent ProblemDetails on unhandled errors.

Six independent fixes, all in or around `src/Alpha.Scada.Gateway/Program.cs` — one commit each or grouped sensibly.

## Fixes

### 1. Parallelize the tags/current fan-in

Lines ~93–94: the TagCatalog and Telemetry calls are awaited sequentially. Send both first, then await both (`Task.WhenAll` or await-after-send). Latency becomes max() of the two instead of the sum.

### 2. Missing current values must not render as 0

Line ~113: `current?.Value ?? 0` — a tag with no stored value shows as `0` with quality `stale`. On a SCADA screen a fabricated zero is dangerous.

- `TagCurrentDto.Value` (`src/Alpha.Scada.Contracts/Telemetry/TelemetryContracts.cs`) → `double?`; pass `current?.Value` (null when absent). `TimestampUtc` similarly should become `DateTimeOffset?` instead of `DateTimeOffset.MinValue` — same fabrication problem.
- Frontend: `src/Alpha.Scada.Web/src/api/types.ts` `Tag.value: number | null`, `timestampUtc: string | null`; render `--` where value is null — touch points: `TagMatrix.tsx`, `lib/format.ts` (`tagValue`, `format` → accept `number | null`), `App.tsx` (`updatedAt` derivation), `OverviewScreen` KPIs, `TrendsScreen` selected-tag display. `applyTelemetryUpdate` in `App.tsx` already replaces value+timestamp together — verify the null-timestamp comparison path (`Date.parse(null)`) is handled: guard with `tag.timestampUtc === null ? accept sample : existing comparison`.

### 3. Validate at the boundary

- `GET /api/tags/{tagId}/history?minutes=`: reject outside `[1, 1440]` with 400 + ProblemDetails (today the gateway forwards anything; only Telemetry clamps).
- `POST /api/reports/monthly/run`: validate `request.Period` when present. Add `MonthPeriod.TryParse(string, out MonthPeriod)` to `src/Alpha.Scada.ServiceDefaults/MonthPeriod.cs` (refactor `Parse` to call it) and return 400 on failure — today a bad period becomes an `InvalidOperationException` deep in the Reporting consumer → 5 retries → error queue.

### 4. Role-gate report runs

Any Viewer can currently trigger report generation. Add to `RoleRules` (`src/Alpha.Scada.Contracts/Auth/AuthContracts.cs`):
```csharp
public static bool CanRunReports(string role) => role is Roles.Admin or Roles.Operator or Roles.SupportEngineer;
```
Check it in `POST /api/reports/monthly/run` → 403 for Viewer. (Don't resurrect `CanManageConfiguration` — Task 10 deletes it; this is a deliberate, narrower rule.) Frontend follow-up (hide the button for Viewers) belongs to Task 17 — backend gate lands here regardless.

### 5. Honest login proxy status

Lines ~58–64: any non-success from Identity becomes 401 — including Identity being down (5xx) or misconfigured, which the user reads as "wrong password". Map: downstream 401 → 401; any other non-success → 502 (log the downstream status).

### 6. ProblemDetails everywhere

Add `UseAlphaExceptionHandling(this IApplicationBuilder app)` to ServiceDefaults: `app.UseExceptionHandler(...)` returning `Results.Problem(statusCode: 500, title: "Unexpected error")` — **no exception details in the body** (they go to logs). Call it first in all nine service `Program.cs` files. Also add `builder.Services.AddProblemDetails()` so framework-generated 400/404s share the shape.

## Tests

- Unit: `MonthPeriod.TryParse` happy/edge cases (extend `MonthPeriodTests`).
- Endpoint-level (in-process TestServer, pattern: `ServiceAuthTests`): history `minutes=0`/`minutes=99999` → 400; report run with `period: "2026-13"` → 400; Viewer token on report run → 403; Operator token → reaches the unit-lookup call.
- Contract: update any test deserializing `TagCurrentDto` for the nullable change (`ContractTests.cs`).
- Frontend: `npx tsc --noEmit` clean after the type change; manually verify a tag with no current value renders `--` (seed by querying a fresh unit before the simulator publishes, or temporarily stop the edge container).

## Constraints

- No route renames; response shapes change only where specified (`TagCurrentDto` nullability).
- Do not add validation libraries — inline checks are fine at this scale.
- The Reporting/Telemetry services keep their own clamps/validation (defense in depth), gateway adds the user-facing layer.

## Verification

```bash
dotnet build Alpha.Scada.slnx && dotnet test Alpha.Scada.slnx
cd src/Alpha.Scada.Web && npx tsc --noEmit && cd -
docker compose up --build
# 400s: curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" "http://localhost:5202/api/tags/<id>/history?minutes=999999"
# 403: viewer@alpha.local triggering POST /api/reports/monthly/run
# 502: docker compose stop identity; login attempt returns 502 not 401; start identity again
# UI: dead tag shows '--', not 0.
```
