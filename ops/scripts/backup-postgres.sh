#!/usr/bin/env bash
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/opt/opspulse/backups}"
COMPOSE_FILE="${COMPOSE_FILE:-/opt/opspulse/docker-compose.prod.yml}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"

mkdir -p "$BACKUP_DIR"
backup_file="$BACKUP_DIR/opspulse-$(date +%Y%m%d-%H%M%S).sql.gz"

docker compose -f "$COMPOSE_FILE" exec -T postgres \
  pg_dump -U "${POSTGRES_USER:-opspulse}" "${POSTGRES_DB:-opspulse}" \
  | gzip > "$backup_file"

find "$BACKUP_DIR" -type f -name "opspulse-*.sql.gz" -mtime +"$RETENTION_DAYS" -delete
echo "Created $backup_file"
