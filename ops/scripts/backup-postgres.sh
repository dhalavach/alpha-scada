#!/usr/bin/env sh
set -eu

: "${POSTGRES_HOST:=localhost}"
: "${POSTGRES_PORT:=5432}"
: "${POSTGRES_DB:=alpha_scada}"
: "${POSTGRES_USER:=alpha}"
: "${BACKUP_DIR:=./backups}"

mkdir -p "$BACKUP_DIR"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
pg_dump "host=$POSTGRES_HOST port=$POSTGRES_PORT dbname=$POSTGRES_DB user=$POSTGRES_USER" \
  --format=custom \
  --file="$BACKUP_DIR/alpha_scada_$timestamp.dump"

echo "$BACKUP_DIR/alpha_scada_$timestamp.dump"
