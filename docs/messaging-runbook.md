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

## Roll Back To HTTP Telemetry Fan-Out

The soak-period fallback is still available while `Edge:LegacyHttpFanOut=true`.

To force the legacy HTTP telemetry path during incident response, keep the
services running and disable their MQTT consumers with a temporary Compose
override:

1. Confirm Edge still has legacy fan-out enabled:

```bash
docker compose exec edge printenv Edge__LegacyHttpFanOut
```

2. Add a temporary override file:

```bash
cat > docker-compose.mqtt-off.yml <<'YAML'
services:
  telemetry:
    environment:
      Mqtt__Enabled: "false"
  asset:
    environment:
      Mqtt__Enabled: "false"
  alarm:
    environment:
      Mqtt__Enabled: "false"
  gateway:
    environment:
      Mqtt__Enabled: "false"
YAML
```

3. Restart the affected services with the override:

```bash
docker compose -f docker-compose.yml -f docker-compose.mqtt-off.yml up -d telemetry asset alarm gateway edge
```

This preserves HTTP telemetry persistence and alarm evaluation through Edge's
legacy fan-out. MQTT-backed alarm/status realtime broadcasts are unavailable
while the override is active, so use the active alarms API/UI refresh as the
operator truth during the rollback window.

Remove `docker-compose.mqtt-off.yml` and restart the same services when MQTT is
healthy again. This rollback is temporary and disappears when the legacy HTTP
fan-out path is deleted.
