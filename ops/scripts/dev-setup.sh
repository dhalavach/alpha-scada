#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
env_file="$root_dir/.env"

touch "$env_file"

ensure_value() {
  local key="$1"
  local value="$2"
  if ! grep -q "^${key}=" "$env_file"; then
    printf '%s=%s\n' "$key" "$value" >> "$env_file"
  fi
}

random_password() {
  openssl rand -hex 24
}

ensure_value "JWT_SECRET" "$(openssl rand -base64 48 | tr -d '\n')"
ensure_value "SERVICE_AUTH_TOKEN" "$(openssl rand -hex 32)"

ensure_value "MQTT_USER_EDGE" "edge"
ensure_value "MQTT_PASSWORD_EDGE" "$(random_password)"
ensure_value "MQTT_USER_EDGE_INGESTOR" "edge-ingestor"
ensure_value "MQTT_PASSWORD_EDGE_INGESTOR" "$(random_password)"
ensure_value "MQTT_USER_TELEMETRY" "telemetry"
ensure_value "MQTT_PASSWORD_TELEMETRY" "$(random_password)"
ensure_value "MQTT_USER_ALARM" "alarm"
ensure_value "MQTT_PASSWORD_ALARM" "$(random_password)"
ensure_value "MQTT_USER_ASSET" "asset"
ensure_value "MQTT_PASSWORD_ASSET" "$(random_password)"
ensure_value "MQTT_USER_GATEWAY" "gateway"
ensure_value "MQTT_PASSWORD_GATEWAY" "$(random_password)"
ensure_value "MQTT_USER_ADMIN" "admin"
ensure_value "MQTT_PASSWORD_ADMIN" "$(random_password)"

"$root_dir/ops/scripts/mosquitto-setup.sh"

echo "Development secrets are ready in $env_file"
