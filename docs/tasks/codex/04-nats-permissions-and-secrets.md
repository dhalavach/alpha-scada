# Task 04 — NATS: per-user permissions, passwords out of git

Read `README.md` in this folder first for repo context.

## Goal

The shared `edge` NATS credential can only publish telemetry/status subjects (no subscriptions to other tenants' data, no forging domain events). NATS passwords are no longer hardcoded in committed files; they flow from the environment.

## Problem

`ops/nats/nats-server.conf` ships three users with literal passwords (`edge-pass`, `services-pass`, `admin-pass`) and **no permissions blocks** — every user has full pub/sub on every subject. Consequences for a multi-tenant SCADA broker:
- The `edge` credential (shared by all field devices) can subscribe to **every tenant's** telemetry and alarms, and can publish forged `alpha.alarm.raised` / `alpha.status.changed` / `alpha.telemetry.stored` domain events that downstream services trust.
- The same literal passwords are committed in: `nats-server.conf`, `.env.example`, `ops/k3s/config.yaml` (a `Secret` manifest in git), and the `Nats:User/Password` sections of 6 `src/*/appsettings.json` files.

Current legitimate traffic per user (verify before changing, see step 4):
- **edge** (`ChpUnitSimulatorWorker`): JetStream-publishes to `alpha.{tenant}.{site}.{unit}.telemetry`. JS publish is a core request → it also needs to **subscribe to its reply inbox** (`_INBOX.>`).
- **services** (Wolverine + `TelemetryAdapterIngestionWorker` + gateway/asset/alarm/reporting): full JetStream usage — `$JS.API.>` requests, `$JS.ACK.>` acks, `_INBOX.>` replies, pub/sub on `alpha.>`, and (after task 03) `$JS.EVENT.ADVISORY.>`.
- **admin**: operator CLI; unrestricted.

## Implementation steps

### 1. Rewrite `ops/nats/nats-server.conf`

NATS server resolves `$VAR` references in config files from the process environment. New authorization section:

```hocon
authorization {
  users = [
    {
      user: "edge"
      password: $NATS_PASSWORD_EDGE
      permissions: {
        publish: ["alpha.*.*.*.telemetry", "alpha.*.*.*.status"]
        subscribe: ["_INBOX.>"]
      }
    }
    {
      user: "services"
      password: $NATS_PASSWORD_SERVICES
      permissions: {
        publish: [">"]
        subscribe: [">"]
      }
    }
    {
      user: "admin"
      password: $NATS_PASSWORD_ADMIN
    }
  ]
}
```

Deliberate scoping decision: `services` stays broad (it is the trusted tier and Wolverine's consumer/inbox subject usage is version-dependent); the isolation win is constraining **edge**. Leave a comment in the conf saying exactly that, so nobody mistakes it for an oversight.

### 2. Feed the passwords to the NATS container

`docker-compose.yml`, `nats` service:
```yaml
  nats:
    image: nats:2.12-alpine
    command: ["-c", "/etc/nats/nats-server.conf"]
    environment:
      NATS_PASSWORD_EDGE: ${NATS_PASSWORD_EDGE:?NATS_PASSWORD_EDGE must be set in .env}
      NATS_PASSWORD_SERVICES: ${NATS_PASSWORD_SERVICES:?NATS_PASSWORD_SERVICES must be set in .env}
      NATS_PASSWORD_ADMIN: ${NATS_PASSWORD_ADMIN:?NATS_PASSWORD_ADMIN must be set in .env}
```
(The service-side `Nats__User/Nats__Password` env vars in compose already come from `.env` — unchanged.)

### 3. Stop committing real-looking passwords

- `ops/scripts/dev-setup.sh`: generate **random** NATS passwords (`openssl rand -hex 16`) instead of the fixed `edge-pass`/`services-pass`/`admin-pass` literals (keep the `ensure_value` idempotency so existing `.env`s aren't clobbered).
- `.env.example`: replace password values with `change-me` placeholders.
- `src/*/appsettings.json` (gateway, asset, telemetry, alarm, reporting, edge): **delete** the `"User"` and `"Password"` keys from the `Nats` section (keep `Url`). Compose/k3s already inject them as env vars. For bare-metal `dotnet run` development, add the dev credentials to each affected project's `appsettings.Development.json` only if local-out-of-docker runs are a workflow you must preserve — and if so, have dev-setup print a reminder that local NATS uses the generated `.env` passwords, so out-of-docker runs should set `Nats__User`/`Nats__Password` env vars instead. Prefer documenting the env-var approach in `docs/dev-setup.md` over committing any password anywhere.
- `ops/k3s/config.yaml`: remove the `stringData` password values from the committed Secret; replace the file's Secret with a commented template and document the imperative step in the file header:
  `kubectl -n alpha-scada create secret generic alpha-scada-secrets --from-literal=Jwt__Secret=... --from-literal=Nats__User=services --from-literal=Nats__Password=...`
  Also check `ops/k3s/nats.yaml` and wire the same env vars into the NATS container there.

### 4. Verify legitimate traffic still flows

Before finalizing the edge permission list, run the stack and watch for `Permissions Violation` errors in `docker compose logs nats`. If the simulator's JetStream publish fails, the missing grant will be named in the error — adjust the edge `subscribe`/`publish` lists minimally (do not fall back to `>`).

## Tests

Extend `tests/Alpha.Scada.Tests/NatsSecurityTests.cs` (it already boots a Testcontainers NATS with an inline conf — reuse that pattern, adding the permissions blocks to the inline conf so the test is self-contained and doesn't depend on env interpolation):

1. Keep: bad credentials rejected; edge can publish `alpha.demo.site.unit.telemetry` (existing assertions).
2. New: **edge cannot forge domain events** — subscribe with `services` to `alpha.alarm.raised`, publish to it with `edge`, assert no message arrives within ~2s (NATS drops denied publishes asynchronously; absence within timeout is the observable).
3. New: **edge cannot snoop** — subscribe with `edge` to `alpha.demo.site.unit.telemetry`, publish there with `services`, assert the edge subscriber receives nothing within ~2s.
4. New: edge **can** still JetStream-publish telemetry and receives a positive PubAck when JetStream + the ALPHA_EDGE stream exist (mirror `NatsTestSupport.PublishAsync` with edge credentials; this proves the `_INBOX.>` subscribe grant suffices).

## Constraints

- Do not enable TLS, accounts, or NKeys in this task (note them as follow-ups in the conf comment if you like).
- Do not change `NatsOptions`/`AlphaMessaging` C# code — this task is config/ops + tests only (the C# already reads user/password from configuration).
- MQTT listener block stays as-is.
- Keep `server_name`, JetStream limits, and ports unchanged.

## Verification

```bash
rm -f .env && ops/scripts/dev-setup.sh        # regenerates with random NATS passwords
grep -rn "edge-pass\|services-pass\|admin-pass" --include="*.json" --include="*.conf" --include="*.yaml" --include="*.yml" src ops .env.example
#   -> no hits (test files with inline confs are exempt if they use obviously-fake values)
docker compose up --build
#   simulator ingests (UI shows live values), no 'Permissions Violation' in: docker compose logs nats | grep -i violation
dotnet test Alpha.Scada.slnx --filter "FullyQualifiedName~NatsSecurity"
```
