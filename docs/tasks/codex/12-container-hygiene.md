# Task 12 — Containers: graceful shutdown, build caching, non-root

Read `README.md` in this folder first for repo context.

## Goal

Services shut down gracefully on SIGTERM (consumers drain, Wolverine flushes), Docker rebuilds reuse the restore layer, containers don't run as root, and compose services restart on failure.

## Problem

`src/Dockerfile.service`:
1. `ENTRYPOINT dotnet "$SERVICE_DLL"` is **shell form** — PID 1 is `/bin/sh`, SIGTERM never reaches the dotnet process. Every `docker compose stop` / k8s rollout waits out the grace period and SIGKILLs mid-flight work (NATS consumers don't drain, in-progress telemetry batches die mid-transaction).
2. `COPY . .` happens before `dotnet restore` — any source change invalidates the restore layer; every rebuild re-restores all packages.
3. Runs as root; no `USER` directive.
4. `docker-compose.yml` has no `restart:` policies, and most `depends_on` entries use `service_started` even where a healthcheck exists.

## Implementation steps

1. **Exec-form entrypoint with env indirection.** The shell form exists only because of the `$SERVICE_DLL` variable. Replace with a tiny launcher:
   ```dockerfile
   COPY src/entrypoint.sh /app/entrypoint.sh
   RUN chmod +x /app/entrypoint.sh
   ENTRYPOINT ["/app/entrypoint.sh"]
   ```
   `src/entrypoint.sh`:
   ```sh
   #!/bin/sh
   exec dotnet "/app/$SERVICE_DLL" "$@"
   ```
   `exec` makes dotnet PID 1 → SIGTERM triggers the .NET host's graceful shutdown.
2. **Layer-cached restore.** Restructure the build stage (requires BuildKit, which is the Docker default):
   ```dockerfile
   # syntax=docker/dockerfile:1.7
   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   ARG PROJECT
   WORKDIR /src
   COPY --parents Alpha.Scada.slnx src/*/*.csproj ./
   RUN dotnet restore "$PROJECT"
   COPY . .
   RUN dotnet publish "$PROJECT" -c Release -o /app/publish --no-restore
   ```
   `COPY --parents` (dockerfile syntax 1.7+) preserves the per-project directory structure. If the local Docker version rejects `--parents`, fall back to the older pattern of restoring against the solution after copying csprojs with `tar`-style staging — but try `--parents` first and note the syntax directive requirement in a comment. If Task 08's `Directory.Build.props`/`Directory.Packages.props` exist by the time this lands, include them in the early COPY (restore depends on them).
3. **Non-root.** The `mcr.microsoft.com/dotnet/aspnet:10.0` runtime image ships a non-root `app` user. In the final stage add `USER app` after the `COPY --from=build`. Port 8080 is unprivileged, so `ASPNETCORE_URLS` keeps working. Verify the published files are world-readable (default `COPY` modes are fine).
4. **No Docker `HEALTHCHECK`** — deliberate: the aspnet base image has no curl/wget, the k3s manifests already do HTTP readiness/liveness probes, and compose health below uses the same constraint. Document this choice in a Dockerfile comment so it isn't "fixed" later by installing curl.
5. **Compose policies.** In `docker-compose.yml`: add `restart: unless-stopped` to all long-running services (not the ops profile); leave `depends_on` conditions as they are except where a `service_healthy` target actually has a healthcheck (currently only postgres — already used).
6. Check `.dockerignore` still covers everything needed (it does today: bin/obj/node_modules/dist/.env) — add `docs/` and `ops/grafana` etc. only if image size measurements justify it; not required.

## Constraints

- No base-image changes, no distroless/chiseled experiments in this task.
- The frontend Dockerfile (`src/Alpha.Scada.Web/Dockerfile`) is out of scope unless it also uses shell-form ENTRYPOINT (check; nginx images are exec-form by default).
- Build args `PROJECT` / `DLL` and the compose service definitions keep working unchanged apart from the additions above.

## Verification

```bash
docker compose build                      # second run after touching one .cs file reuses the restore layer (watch the cache hits)
docker compose up -d
docker compose exec telemetry sh -c 'echo PID1: && cat /proc/1/cmdline | tr "\0" " "'   # dotnet, not sh
docker inspect $(docker compose ps -q telemetry) --format '{{.Config.User}}'            # app
# Graceful shutdown: watch logs while stopping —
docker compose stop telemetry            # logs show "Application is shutting down..." style host shutdown, exit code 0, well under the 10s grace
docker compose up -d && docker compose restart gateway   # UI recovers, restart policy present in `docker inspect`
dotnet test Alpha.Scada.slnx             # unaffected, green
```
