# Development Setup

Run the setup helper once before using Docker Compose:

```bash
ops/scripts/dev-setup.sh
```

It creates a gitignored `.env` file with:

```text
JWT_SECRET
MQTT_USER_* and MQTT_PASSWORD_*
```

It also generates `ops/mosquitto/passwords`, which is required because the local Mosquitto broker runs with anonymous access disabled and ACLs enabled.

The default Compose stack enables development demo users in the Identity service.

Demo credentials:

```text
admin@alpha.local / ChangeMe!123
operator@alpha.local / ChangeMe!123
viewer@alpha.local / ChangeMe!123
support@alpha.local / ChangeMe!123
```

For production-like runs, do not set `Seed__DemoUsers=true`. If the identity database is empty, the Identity service creates `bootstrap-admin@local` with a random temporary password and logs it once at startup. Rotate that credential immediately.
