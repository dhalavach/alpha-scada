# Task 17 — Frontend robustness pass

Read `README.md` in this folder first for repo context. All work in `src/Alpha.Scada.Web` unless noted.

## Goal

The SPA survives token expiry and backend failures gracefully, hides actions the user's role can't perform, has linting and honest dependency hygiene, uses union types for domain enums, derives the Overview layout from tag metadata instead of hardcoded demo keys, and stops exposing `/metrics` publicly.

## Problems & fixes

### 1. Expired-token and error handling

A 12h token expiring mid-shift currently leaves a silently broken dashboard: `getJson` throws a generic `Error`, `loadInitial()` is fired un-awaited without catch (`App.tsx:60-63`), and nothing reacts to 401.

- `api/client.ts`: introduce `export class ApiError extends Error { constructor(public status: number, path: string) ... }`; `getJson` (and a new `postJson` if it reduces duplication in `App.tsx`) throws it with the response status. Add an `onUnauthorized` hook: the simplest robust wiring is a module-level callback set once by `App` (`setUnauthorizedHandler(() => logout())`) invoked whenever `status === 401`.
- `App.tsx`: wrap `loadInitial` in try/catch → error state with a visible banner + Retry button; 401 anywhere → `logout()` (clears token → Login screen renders).
- Add an `ErrorBoundary` class component wrapping the workspace (render a panel with the error message and a reload button). React 19: class boundaries still required for error catching.

### 2. Role-gated UI

`user.role` is already loaded. Mirror the backend rules (`RoleRules`):
- Ack buttons (`AlarmPreview`, `AlarmsScreen`): render only for `Admin | Operator | SupportEngineer`.
- "Run report" buttons (`OverviewScreen`/`ReportPreview`, `ReportsScreen`): same set (matches Task 13's backend `CanRunReports` gate).
- `Admin` nav item: render only for `Admin | SupportEngineer`.
Implement as a small `lib/roles.ts` (`canAcknowledge(role)`, `canRunReports(role)`, `canViewAdmin(role)`) so the rules live in one place. Pass `user` down where needed (it's already in `App`).

### 3. Dependency and tooling hygiene

- `package.json`: move `vite`, `typescript`, `@vitejs/plugin-react` to `devDependencies` (runtime deps remain `react`, `react-dom`, `@microsoft/signalr`).
- Add ESLint (flat config): `eslint`, `typescript-eslint`, `eslint-plugin-react-hooks`. Scripts: `"lint": "eslint src"`, `"typecheck": "tsc --noEmit"`. Fix what the default recommended rules flag (expect minor unused-var/hook-deps findings); do not disable rules wholesale.
- If Task 08's CI exists, add `npm run lint` to the frontend job.

### 4. Union types in `api/types.ts`

Replace stringly-typed enums with unions that match backend values, using the open-union idiom so unknown values don't explode:
```ts
export type Quality = "good" | "stale" | "aggregated" | (string & {});
export type Severity = "critical" | "warning" | (string & {});
export type AlarmState = "active" | "acknowledged" | "cleared" | (string & {});
export type UnitStatus = "online" | "offline" | "unknown" | (string & {});
```
Apply to `Tag.quality`, `HistoryPoint.quality`, `Alarm.severity/state`, `Unit.status`, `Site.status`, `TelemetryUpdateSample.quality`. Fix any comparisons the compiler then flags.

### 5. Derive the Overview process flow from metadata

`lib/format.ts` hardcodes five demo tag keys (`processSteps`); any non-demo unit renders `--` across the whole process strip and KPI row.

- Keep a `PREFERRED_PROCESS_KEYS` list (the current five) as a *preference*, not a requirement: `buildProcessSteps(tags)` returns preferred keys that exist in the unit's tags; if fewer than 2 match, fall back to the first tag of each subsystem in catalog order (the API returns tags ordered by subsystem, name), capped at 5 steps. Each step's label = subsystem (fallback path) or the current friendly label (preferred path); unit = the tag's `engineeringUnit`.
- Same approach for the four KPI cards in `OverviewScreen`: preferred keys when present, else the first four tags.
- Delete the now-unused static `processSteps` export.

### 6. Stop exposing `/metrics` through the public frontend

- `AdminScreen.tsx`: remove the raw metrics `<pre>` panel; keep the Health/Readiness probes. `App.tsx loadSystem`: drop the `/metrics` fetch; adjust `SystemProbe`.
- `nginx.conf`: remove the `/metrics` location block. While in the file: add `gzip on; gzip_types text/css application/javascript application/json;`, security headers (`X-Content-Type-Options nosniff`, `X-Frame-Options DENY`, `Referrer-Policy no-referrer`), and asset caching (`location /assets/ { expires 7d; add_header Cache-Control "public, immutable"; }` — Vite emits hashed filenames under `/assets/`; `index.html` itself must stay `no-cache`).

## Constraints

- No router, no data-fetching library, no state-management library in this task — structure only what the fixes require.
- No visual redesign; the demo unit's Overview must look unchanged (preferred keys all match).
- Don't add frontend tests in this task (kept scoped; CI runs lint + typecheck).

## Verification

```bash
cd src/Alpha.Scada.Web && npm ci && npm run lint && npm run typecheck && npm run build
docker compose up --build
# - Expire simulation: localStorage token replaced with garbage → next API call logs you out to Login (no white screen).
# - Stop gateway mid-session → error banner with Retry; start gateway, Retry recovers.
# - viewer@alpha.local: no Ack buttons, no Run report, no Admin nav. operator: Ack+Run visible, no Admin.
# - curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/metrics   -> 404
# - Demo unit Overview renders the same five process steps as before.
```
