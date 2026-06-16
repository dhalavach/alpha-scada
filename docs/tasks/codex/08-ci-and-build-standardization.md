# Task 08 — CI pipeline + build standardization (Directory.Build.props, central package management)

Read `README.md` in this folder first for repo context.

## Goal

Every push/PR is gated by an automated build + full test run + frontend typecheck. MSBuild settings and package versions live in one place. Warnings fail the build.

## Problem

- There is **no CI at all** (no `.github/` directory). Build health, the 0-warnings status, and the now-deterministic test suite are conventions, not gates.
- All 16 `.csproj` files repeat `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, and pin package versions independently (e.g. `WolverineFx` 5.21.0 in two places, `Npgsql` in several). No `Directory.Build.props` / `Directory.Packages.props`.

## Implementation steps

1. **`Directory.Build.props`** at repo root:
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
     </PropertyGroup>
   </Project>
   ```
   Remove those four properties from every csproj (keep project-specific ones like `IsPackable`). The build is currently at 0 warnings, so `TreatWarningsAsErrors` is safe to enable — if any warning appears during this task, fix it, don't suppress it.
2. **Central package management**: `Directory.Packages.props` with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` and one `<PackageVersion>` entry per package (collect the union from all csprojs; on version conflicts pick the highest currently used). Strip `Version=` attributes from all `<PackageReference>`s.
3. **GitHub Actions workflow** `.github/workflows/ci.yml`, triggered on `push` to `main` and `pull_request`:
   - Job `backend` (ubuntu-latest): checkout; `actions/setup-dotnet` with `10.0.x`; pre-pull the Testcontainers images exactly as documented in `docs/dev-setup.md` (`docker pull timescale/timescaledb:2.17.2-pg16`, `docker pull nats:2.12-alpine` — read the tags from `tests/Alpha.Scada.Tests/TestImages.cs`, don't hardcode blindly); `dotnet build Alpha.Scada.slnx`; `dotnet test Alpha.Scada.slnx --no-build --logger "trx"`; upload test results artifact on failure. The ubuntu runner has Docker available; container tests must **pass**, not skip — fail the job if the skipped count is unexpectedly high (e.g. assert via a small script on the trx, or at minimum print the summary; do not silently accept all-skipped).
   - Job `frontend` (ubuntu-latest): `actions/setup-node` with Node 22 + npm cache keyed on `src/Alpha.Scada.Web/package-lock.json`; `npm ci`; `npx tsc --noEmit`; `npm run build` (working dir `src/Alpha.Scada.Web`).
   - Job `docker` (optional, can be `workflow_dispatch`-only to save minutes): `docker compose build`.
4. Add a CI status badge to `README.md`.

## Constraints

- Do not change package versions beyond unifying duplicates (no upgrades in this task).
- Do not touch test code (Task 06 already made the suite deterministic).
- Keep the workflow minimal — no release/publish jobs, no codecov, no matrix.

## Verification

```bash
dotnet build Alpha.Scada.slnx       # 0 warnings, now enforced as errors
dotnet test Alpha.Scada.slnx        # green
git grep -n "<TargetFramework>" -- "src/*.csproj" "tests/*.csproj"   # no matches
git grep -n "Version=\"" -- "src/*.csproj" "tests/*.csproj"          # no matches
```
Push the branch and confirm both CI jobs pass on the PR before merging.
