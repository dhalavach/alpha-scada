# Task 06 — De-flake the test suite (parallel Testcontainers stampede)

Read `README.md` in this folder first for repo context.

## Goal

`dotnet test Alpha.Scada.slnx` passes deterministically: three consecutive runs from a cold Docker state are green, and on a machine without Docker the container-backed tests **skip** (never fail).

## Problem

Observed on 2026-06-10 (macOS, Docker Desktop):
- Cold run: **25/92 failed in ~0.4s total** — instant failures while many Testcontainers (Postgres, NATS) tried to start/pull in parallel.
- Immediately-following run: 1/92 failed (`TelemetryPrimaryIngestionTests.Native_nats_telemetry_is_normalized_into_primary_storage_and_published_as_domain_event`).
- Every failing test **passes when run in isolation** via `--filter`.

Root causes to address:
1. xUnit runs test collections in parallel; each integration test class (and sometimes each test) builds its own Postgres and/or NATS container inline → cold-start image pulls and Docker daemon contention.
2. The Docker-unavailable → `SkipException` guard exists in some classes (e.g. `CommunicationLossAlarmTests` catches `DockerUnavailableException`) but **not consistently** — e.g. `ReportOntologyConfigTests`, `ServiceDefaultsEndpointTests`, `BackendRepositoryTests` evidently fail instead of skipping when container startup misbehaves.

Inventory the container-using test classes first: `grep -ln "ContainerBuilder\|NatsTestSupport.StartAsync" tests/Alpha.Scada.Tests/*.cs`.

## Implementation steps

### 1. Serialize container-backed collections (the reliability floor)

Add `tests/Alpha.Scada.Tests/xunit.runner.json`:
```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "parallelizeTestCollections": false
}
```
and in the csproj:
```xml
<ItemGroup>
  <None Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
This alone removes the stampede. It costs wall-clock time for the pure-unit tests too — acceptable, but step 3 wins most of it back. (Alternative considered and rejected: per-class `[Collection]` attributes with `DisableTestParallelization` — more invasive, same effect.)

### 2. Make "Docker unavailable" a consistent skip

Add a small helper to `tests/Alpha.Scada.Tests/` (e.g. `ContainerSupport.cs`):

```csharp
internal static class ContainerSupport
{
    public static async Task<T> StartOrSkipAsync<T>(Func<Task<T>> start, string what)
    {
        try
        {
            return await start();
        }
        catch (Exception ex) when (IsDockerUnavailable(ex))
        {
            throw SkipException.ForSkip($"Docker is not available for {what}: {ex.Message}");
        }
    }

    private static bool IsDockerUnavailable(Exception ex) =>
        ex is DockerUnavailableException
        || ex.InnerException is DockerUnavailableException
        || ex is TimeoutException; // container wait-strategy timeout on overloaded daemon
}
```

Route **every** container start in the test project through it (Postgres `ContainerBuilder().Build()` starts and `NatsTestSupport.StartAsync`). Review what exception actually surfaced in the failing classes before finalizing `IsDockerUnavailable` — extend the `when` clause to the observed types, but do **not** swallow assertion failures or app exceptions.

### 3. Shared container fixtures (the speed win)

Introduce xUnit collection fixtures so one Postgres and one NATS container serve the whole run:

1. `PostgresContainerFixture : IAsyncLifetime` — starts one TimescaleDB container (`TestImages.Postgres`); exposes `string AdminConnectionString` and `async Task<string> CreateDatabaseAsync(string prefix)` that runs `CREATE DATABASE {prefix}_{8-char-suffix}` and returns its connection string. Per-test databases keep isolation; creating a database is milliseconds vs ~5–10s per container.
2. `NatsContainerFixture : IAsyncLifetime` — wraps the existing `NatsTestSupport.StartAsync` conf. NATS state is trickier to share because streams/consumers/dedup windows persist; keep isolation by making **subjects and durable names unique per test** where the test creates them, and where a test needs pristine JetStream state (e.g. asserting stream creation), let it purge or delete the streams it uses in setup. If a specific test truly needs a private broker (e.g. `NatsSecurityTests` with its custom authorization conf), leave it standalone — that's fine.
3. `[CollectionDefinition("containers")]` with both fixtures; mark the container-backed classes `[Collection("containers")]`.
4. Migrators in these tests run `create table if not exists` etc. per fresh database, so they keep working unchanged; tests that currently assert global row counts (e.g. `CountRowsAsync(... "alarm_events")`) are safe because each test now owns a fresh database.

Convert the heaviest classes first (`CommunicationLossAlarmTests`, `TelemetryPrimaryIngestionTests`, `AlarmOutboxTests`, `BackendRepositoryTests`, `AssetRepositoryBehaviorTests`, `ReportOntologyConfigTests`, `ServiceDefaultsEndpointTests`). If one resists conversion, leave it standalone — step 1 already serializes it.

### 4. CI prewarm note

Add to `docs/dev-setup.md` (testing section): pull `timescale/timescaledb:…` and `nats:…` images (exact tags from `tests/Alpha.Scada.Tests/TestImages.cs`) before the first test run; in CI this should be an explicit `docker pull` step.

## Constraints

- Do not weaken any assertion or delete any test.
- Do not add new test frameworks/packages (xunit + Testcontainers stay; `Xunit.SkipException` from xunit 2.9 / `Xunit.Sdk` is already in use).
- Keep tests runnable individually via `--filter` (fixtures must not assume the full collection runs).
- Total warm-run wall time must not exceed ~2× today's (~46s warm full run).

## Verification

```bash
# Cold-state determinism: clear local images for the test tags first (or docker system prune), then:
for i in 1 2 3; do dotnet test Alpha.Scada.slnx --nologo || echo "RUN $i FAILED"; done
#   -> three green runs, no "RUN n FAILED"
# No-Docker behavior (e.g. stop Docker Desktop): container tests report Skipped, exit code 0.
# Spot-check isolation still works:
dotnet test Alpha.Scada.slnx --filter "FullyQualifiedName~CommunicationLoss"
```

In the PR description, report the before/after timings and the final passed/skipped counts for both the Docker and no-Docker modes.
