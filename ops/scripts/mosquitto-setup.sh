#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
env_file="$root_dir/.env"
config_dir="$root_dir/ops/mosquitto"
password_file="$config_dir/passwords"

if [[ ! -f "$env_file" ]]; then
  echo "Missing $env_file. Run ops/scripts/dev-setup.sh first." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
. "$env_file"
set +a

write_passwords_local() {
  local create_flag="-c"
  rm -f "$password_file"
  for entry in "$@"; do
    local user="${entry%%:*}"
    local password="${entry#*:}"
    if [[ -n "$create_flag" ]]; then
      mosquitto_passwd -b "$create_flag" "$password_file" "$user" "$password"
    else
      mosquitto_passwd -b "$password_file" "$user" "$password"
    fi
    create_flag=""
  done
}

write_passwords_docker() {
  local create_flag="-c"
  rm -f "$password_file"
  for entry in "$@"; do
    local user="${entry%%:*}"
    local password="${entry#*:}"
    if [[ -n "$create_flag" ]]; then
      docker run --rm \
        -v "$config_dir:/mosquitto/config" \
        eclipse-mosquitto:2 \
        mosquitto_passwd -b "$create_flag" /mosquitto/config/passwords "$user" "$password"
    else
      docker run --rm \
        -v "$config_dir:/mosquitto/config" \
        eclipse-mosquitto:2 \
        mosquitto_passwd -b /mosquitto/config/passwords "$user" "$password"
    fi
    create_flag=""
  done
}

entries=(
  "${MQTT_USER_EDGE:?MQTT_USER_EDGE is required}:${MQTT_PASSWORD_EDGE:?MQTT_PASSWORD_EDGE is required}"
  "${MQTT_USER_EDGE_INGESTOR:?MQTT_USER_EDGE_INGESTOR is required}:${MQTT_PASSWORD_EDGE_INGESTOR:?MQTT_PASSWORD_EDGE_INGESTOR is required}"
  "${MQTT_USER_TELEMETRY:?MQTT_USER_TELEMETRY is required}:${MQTT_PASSWORD_TELEMETRY:?MQTT_PASSWORD_TELEMETRY is required}"
  "${MQTT_USER_ALARM:?MQTT_USER_ALARM is required}:${MQTT_PASSWORD_ALARM:?MQTT_PASSWORD_ALARM is required}"
  "${MQTT_USER_ASSET:?MQTT_USER_ASSET is required}:${MQTT_PASSWORD_ASSET:?MQTT_PASSWORD_ASSET is required}"
  "${MQTT_USER_GATEWAY:?MQTT_USER_GATEWAY is required}:${MQTT_PASSWORD_GATEWAY:?MQTT_PASSWORD_GATEWAY is required}"
  "${MQTT_USER_ADMIN:?MQTT_USER_ADMIN is required}:${MQTT_PASSWORD_ADMIN:?MQTT_PASSWORD_ADMIN is required}"
)

mkdir -p "$config_dir"

if command -v mosquitto_passwd >/dev/null 2>&1; then
  write_passwords_local "${entries[@]}"
elif command -v docker >/dev/null 2>&1; then
  write_passwords_docker "${entries[@]}"
else
  echo "Need either mosquitto_passwd or docker to generate $password_file." >&2
  exit 1
fi

chmod 600 "$password_file"
echo "Mosquitto password file is ready at $password_file"
