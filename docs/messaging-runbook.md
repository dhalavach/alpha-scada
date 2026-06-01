# Messaging Runbook

This runbook covers the v1 messaging path:

Edge/adapter MQTT telemetry -> Telemetry normalization -> `TelemetryBatchStored` -> Asset/Alarm -> Gateway SignalR.

## Quick Health Check

```bash
docker compose ps
curl -fsS http://localhost:5202/health
curl -fsS http://localhost:8080/
```

Check Prometheus targets:

```bash
docker compose --profile ops up -d prometheus grafana
open http://localhost:3000
```

Each service exports app-side Wolverine metrics at `/metrics`, including
outbox depth, error queue depth, and telemetry sample count where applicable.
Broker-side MQTT DLQ depth and subscriber lag still require a dedicated
Mosquitto exporter or log-derived metric in a later operations slice.

## MQTT DLQ Is Non-Empty

Inspect messages:

```bash
set -a
source .env
set +a
docker compose exec -T mosquitto mosquitto_sub \
  -h localhost \
  -t 'alpha/_dlq/#' \
  -u "$MQTT_USER_ADMIN" \
  -P "$MQTT_PASSWORD_ADMIN" \
  -C 10 \
  -W 10 \
  -v
```

Triage:

1. Identify the owning service from the topic: `alpha/_dlq/<service>/...`.
2. Check that service logs: `docker compose logs --tail 200 <service>`.
3. Compare the message schema version to `docs/architecture-decisions/002-messaging.md`.
4. If the payload is valid but the consumer is down, restart only the consumer service.
5. If the payload is invalid, save the message body with the incident record and discard it after approval.

Replay is manual in v1. Publish the corrected payload to the original topic with `mosquitto_pub`.

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
- `alpha_gateway` is not used in v1

Triage:

1. Fix the consumer error shown in `exception_message`.
2. Restart the affected service.
3. Replay or discard dead letters manually after confirming the handler is idempotent.

## Outbox Depth Is Climbing

Check pending outgoing messages:

```bash
docker compose exec -T postgres psql -U alpha -d alpha_telemetry -c \
  "select destination, message_type, count(*) as pending, min(deliver_by) as oldest_due
   from wolverine.wolverine_outgoing_envelopes
   group by destination, message_type
   order by pending desc;"
```

Diagnosis:

- Broker down: `docker compose ps mosquitto` and `docker compose logs --tail 100 mosquitto`.
- Consumer down: `docker compose ps telemetry asset alarm gateway`.
- Schema mismatch: check consumer logs for version or JSON mapping failures.
- Slow handler: check the service database and downstream HTTP dependencies.

## Reporting Queue Is Backed Up

Reporting requests use PostgreSQL-backed Wolverine queues:

```bash
docker compose exec -T postgres psql -U alpha -d alpha_reporting -c \
  "select 'requested' as queue, count(*) from wolverine_queues.wolverine_queue_reports_requested
   union all
   select 'completed' as queue, count(*) from wolverine_queues.wolverine_queue_reports_completed;"
```

If `reports_requested` grows, restart `reporting`. If `reports_completed` grows, restart `gateway`.

## Roll Back Messaging Cleanup

The legacy HTTP telemetry fan-out path has been removed. After this cleanup,
rollback is a code rollback, not a runtime flag:

```bash
git revert <cleanup-commit>
docker compose up --build -d
```

Use this only if the MQTT normalization path itself is defective. For broker or
consumer outages, prefer the DLQ, outbox, and service-restart procedures above
so telemetry is replayed through the durable inbox/outbox path.
