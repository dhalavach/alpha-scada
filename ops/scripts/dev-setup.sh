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

ensure_value "JWT_SECRET" "$(openssl rand -base64 48 | tr -d '\n')"

ensure_value "NATS_USER_EDGE" "edge"
ensure_value "NATS_PASSWORD_EDGE" "$(openssl rand -hex 16)"
ensure_value "NATS_USER_SERVICES" "services"
ensure_value "NATS_PASSWORD_SERVICES" "$(openssl rand -hex 16)"
ensure_value "NATS_USER_ADMIN" "admin"
ensure_value "NATS_PASSWORD_ADMIN" "$(openssl rand -hex 16)"

echo "Development secrets are ready in $env_file"
