#!/usr/bin/env sh
set -eu

if [ "${1:-}" = "" ] || [ "${2:-}" = "" ]; then
  echo "usage: $0 database_name path/to/backup.dump" >&2
  exit 1
fi

: "${POSTGRES_HOST:=localhost}"
: "${POSTGRES_PORT:=5432}"
: "${POSTGRES_USER:=alpha}"

database="$1"
backup_file="$2"

pg_restore "host=$POSTGRES_HOST port=$POSTGRES_PORT dbname=$database user=$POSTGRES_USER" \
  --clean \
  --if-exists \
  --no-owner \
  "$backup_file"
