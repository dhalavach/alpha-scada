# Development Setup

Run the setup helper once before using Docker Compose:

```bash
ops/scripts/dev-setup.sh
```

It creates a gitignored `.env` file with:

```text
JWT_SECRET
NATS_USER_* and NATS_PASSWORD_*
```

The local NATS config uses simple development users for the edge MQTT listener and service-to-service NATS clients. Replace them with deployment-managed secrets before a customer rollout.

The default Compose stack enables development demo users in the Identity service.

Demo credentials:

```text
admin@alpha.local / ChangeMe!123
operator@alpha.local / ChangeMe!123
viewer@alpha.local / ChangeMe!123
support@alpha.local / ChangeMe!123
```

For production-like runs, do not set `Seed__DemoUsers=true`. If the identity database is empty, the Identity service creates `bootstrap-admin@local` with a random temporary password and logs it once at startup. Rotate that credential immediately.

## Test Container Prewarm

The integration tests use Testcontainers for TimescaleDB/PostgreSQL and NATS. Before a first cold test run, pre-pull the exact images used by the suite:

```bash
docker pull timescale/timescaledb:2.17.2-pg16
docker pull nats:2.12-alpine
```

This avoids Docker Desktop doing image pulls while xUnit is trying to start containers.
