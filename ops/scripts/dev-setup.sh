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

read_value() {
  local key="$1"
  grep "^${key}=" "$env_file" | tail -n 1 | cut -d= -f2- || true
}

ensure_jwt_key_pair() {
  local private_key_b64
  local public_key_b64
  local derived_public_b64
  local private_key_file

  private_key_b64="$(read_value "JWT_PRIVATE_KEY_B64")"
  public_key_b64="$(read_value "JWT_PUBLIC_KEY_B64")"
  private_key_file="$(mktemp)"

  if [[ -z "$private_key_b64" && -z "$public_key_b64" ]]; then
    openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$private_key_file" >/dev/null 2>&1
    private_key_b64="$(openssl base64 -A -in "$private_key_file")"
    public_key_b64="$(openssl pkey -in "$private_key_file" -pubout 2>/dev/null | openssl base64 -A)"
    ensure_value "JWT_PRIVATE_KEY_B64" "$private_key_b64"
    ensure_value "JWT_PUBLIC_KEY_B64" "$public_key_b64"
  elif [[ -n "$private_key_b64" ]]; then
    printf '%s' "$private_key_b64" | openssl base64 -d -A > "$private_key_file"
    derived_public_b64="$(openssl pkey -in "$private_key_file" -pubout 2>/dev/null | openssl base64 -A)"
    if [[ -z "$public_key_b64" ]]; then
      ensure_value "JWT_PUBLIC_KEY_B64" "$derived_public_b64"
    elif [[ "$public_key_b64" != "$derived_public_b64" ]]; then
      rm -f "$private_key_file"
      echo "JWT_PRIVATE_KEY_B64 and JWT_PUBLIC_KEY_B64 in $env_file do not form a matching key pair." >&2
      exit 1
    fi
  else
    rm -f "$private_key_file"
    echo "JWT_PUBLIC_KEY_B64 exists without JWT_PRIVATE_KEY_B64 in $env_file; refusing to overwrite it." >&2
    exit 1
  fi

  rm -f "$private_key_file"
}

ensure_jwt_key_pair

ensure_value "NATS_USER_EDGE" "edge"
ensure_value "NATS_PASSWORD_EDGE" "$(openssl rand -hex 16)"
ensure_value "NATS_USER_SERVICES" "services"
ensure_value "NATS_PASSWORD_SERVICES" "$(openssl rand -hex 16)"
ensure_value "NATS_USER_ADMIN" "admin"
ensure_value "NATS_PASSWORD_ADMIN" "$(openssl rand -hex 16)"

echo "Development secrets are ready in $env_file"
