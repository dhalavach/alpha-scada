# Messaging Runbook

This runbook covers the v1 messaging path:

Edge/adapter MQTT telemetry -> NATS MQTT listener -> Telemetry normalization -> `TelemetryBatchStored` -> Asset/Alarm -> Gateway SignalR.

## Quick Health Check

```bash
docker compose ps
curl -fsS http://localhost:5202/health
curl -fsS http://localhost:8080/
curl -fsS http://localhost:8222/varz
```

Check Prometheus targets:

```bash
docker compose --profile ops up -d prometheus grafana
open http://localhost:3000
```

Each service exports app-side Wolverine metrics at `/metrics`, including outbox depth, error queue depth, and telemetry sample count where applicable. NATS monitoring is available on `http://localhost:8222`.

## JetStream Streams

Inspect streams:

```bash
curl -fsS "http://localhost:8222/jsz?streams=true&consumers=true" | jq
```

Expected streams:

- `ALPHA_EDGE`: raw edge ingress and Sparkplug-ready subjects.
- `ALPHA_DOMAIN`: normalized telemetry, status, alarm, and report-completed events.
- `ALPHA_JOBS`: report request work queue.

## Wolverine Error Queue Is Non-Empty

Inspect dead letters for a service database:

```bash
docker compose exec -T postgres psql -U alpha -d alpha_alarm -c \
  "select id, message_type, source, exception_type, exception_message, sent_at
   from wolverine.wolverine_dead_letters
   order by sent_at desc
   limit 20;"
```

Use the relevant database:

- `alpha_telemetry`
- `alpha_asset`
- `alpha_alarm`
- `alpha_reporting`

Triage:

1. Fix the consumer error shown in `exception_message`.
2. Restart the affected service.
3. Replay or discard dead letters manually after confirming the handler is idempotent.

## Messaging Backlog Is Climbing

Check Wolverine dead letters first:

```bash
docker compose exec -T postgres psql -U alpha -d alpha_telemetry -c \
  "select message_type, count(*) as failed, max(sent_at) as latest_failure
   from wolverine.wolverine_dead_letters
   group by message_type
   order by failed desc;"
```

Diagnosis:

- Broker down: `docker compose ps nats` and `docker compose logs --tail 100 nats`.
- Consumer down: `docker compose ps telemetry asset alarm gateway reporting`.
- Edge ingress stalled: `docker compose logs --tail 100 telemetry` and inspect the `ALPHA_EDGE` stream/consumer in NATS monitoring.
- Schema mismatch: check consumer logs for version or JSON mapping failures.
- Slow handler: check the service database and downstream HTTP dependencies.

## Reporting Jobs Are Backed Up

Report jobs use `alpha.report.requested` in the `ALPHA_JOBS` stream:

```bash
curl -fsS "http://localhost:8222/jsz?streams=true&consumers=true" | jq '.account_details[].stream_detail[] | select(.name=="ALPHA_JOBS")'
```

If pending messages grow, restart `reporting`. If completed events are not reaching the UI, restart `gateway` and inspect `ALPHA_DOMAIN`.

## Raw Telemetry Is Not Flowing

Inspect the edge stream:

```bash
curl -fsS "http://localhost:8222/jsz?streams=true&consumers=true" | jq '.account_details[].stream_detail[] | select(.name=="ALPHA_EDGE")'
docker compose logs --tail 200 telemetry
docker compose logs --tail 200 edge
```

Common causes:

- Edge publisher credentials do not match the NATS MQTT listener.
- The topic does not match `alpha/{tenant}/{site}/{unit}/telemetry`.
- The tenant/site/unit/tag keys do not resolve through Tenant, Asset, or Tag Catalog.
- The payload schema version has an unsupported major version.

## Roll Back Messaging Changes

The legacy broker/queue path has been removed. Rollback is a code rollback, not a runtime flag:

```bash
git revert <migration-commit>
docker compose up --build -d
```

Use rollback only if the NATS normalization path itself is defective. For broker or consumer outages, prefer the JetStream, Wolverine error queue, and service-restart procedures above.
