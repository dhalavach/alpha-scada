#!/usr/bin/env sh
set -eu

if [ "${1:-}" = "" ]; then
  echo "usage: $0 path/to/backup.dump" >&2
  exit 1
fi

: "${POSTGRES_HOST:=localhost}"
: "${POSTGRES_PORT:=5432}"
: "${POSTGRES_DB:=alpha_scada}"
: "${POSTGRES_USER:=alpha}"

pg_restore "host=$POSTGRES_HOST port=$POSTGRES_PORT dbname=$POSTGRES_DB user=$POSTGRES_USER" \
  --clean \
  --if-exists \
  --no-owner \
  "$1"
