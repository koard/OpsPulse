#!/usr/bin/env bash
set -euo pipefail

PROJECT_ID="${PROJECT_ID:-university-portal}"
TELEMETRY_URL="${TELEMETRY_URL:-http://localhost:5082/snapshots}"

timestamp="$(date --iso-8601=seconds)"
cpu_percent="$(top -bn1 | awk '/Cpu/ { print 100 - $8 }')"
memory_percent="$(free | awk '/Mem:/ { printf "%.1f", $3 / $2 * 100 }')"
disk_percent="$(df / | awk 'NR==2 { gsub("%", "", $5); print $5 }')"

processes="$(pm2 jlist | jq '[.[] | {
  name: .name,
  role: (.pm2_env.name // .name),
  status: .pm2_env.status,
  cpuPercent: (.monit.cpu // 0),
  memoryMb: ((.monit.memory // 0) / 1024 / 1024 | floor),
  restarts: (.pm2_env.restart_time // 0),
  uptimeSeconds: ((now - ((.pm2_env.pm_uptime // 0) / 1000)) | floor)
}]')"

payload="$(jq -n \
  --arg projectId "$PROJECT_ID" \
  --arg capturedAt "$timestamp" \
  --argjson cpu "$cpu_percent" \
  --argjson memory "$memory_percent" \
  --argjson disk "$disk_percent" \
  --argjson processes "$processes" \
  '{
    projectId: $projectId,
    capturedAt: $capturedAt,
    metrics: {
      cpuPercent: $cpu,
      memoryPercent: $memory,
      diskPercent: $disk,
      networkInMbps: 0,
      networkOutMbps: 0
    },
    processes: $processes,
    endpoints: []
  }')"

curl -fsS -X POST "$TELEMETRY_URL" \
  -H "Content-Type: application/json" \
  -d "$payload"
