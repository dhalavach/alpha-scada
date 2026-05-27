#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
env_file="$root_dir/.env"

touch "$env_file"

if ! grep -q '^JWT_SECRET=' "$env_file"; then
  jwt_secret="$(openssl rand -base64 48 | tr -d '\n')"
  printf 'JWT_SECRET=%s\n' "$jwt_secret" >> "$env_file"
fi

if ! grep -q '^SERVICE_AUTH_TOKEN=' "$env_file"; then
  service_token="$(openssl rand -hex 32)"
  printf 'SERVICE_AUTH_TOKEN=%s\n' "$service_token" >> "$env_file"
fi

echo "Development secrets are ready in $env_file"
