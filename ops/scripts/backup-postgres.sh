#!/usr/bin/env sh
set -eu

: "${POSTGRES_HOST:=localhost}"
: "${POSTGRES_PORT:=5432}"
: "${POSTGRES_DATABASES:=alpha_identity alpha_tenant alpha_asset alpha_tag_catalog alpha_edge alpha_telemetry alpha_alarm alpha_reporting}"
: "${POSTGRES_USER:=alpha}"
: "${BACKUP_DIR:=./backups}"

mkdir -p "$BACKUP_DIR"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"

for database in $POSTGRES_DATABASES; do
  pg_dump "host=$POSTGRES_HOST port=$POSTGRES_PORT dbname=$database user=$POSTGRES_USER" \
    --format=custom \
    --file="$BACKUP_DIR/${database}_$timestamp.dump"
  echo "$BACKUP_DIR/${database}_$timestamp.dump"
done
