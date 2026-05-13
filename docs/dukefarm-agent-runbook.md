# DukeFarm OpsPulse Agent Runbook

The agent runs on the DukeFarm Ubuntu server as its own PM2 process. It does not modify DukeFarm backend code.

## Requirements

- Node.js 22+
- PM2
- Bash tools: `top`, `free`, `df`
- DukeFarm backend reachable at `http://127.0.0.1:4000`
- OpsPulse reachable at `https://ops.example.com`

## Install

```bash
sudo mkdir -p /opt/opspulse-agent
sudo chown "$USER:$USER" /opt/opspulse-agent
cp -r agent/opspulse-agent/* /opt/opspulse-agent/
cd /opt/opspulse-agent
cp .env.example .env
chmod 600 .env
```

Edit `.env`:

```bash
PROJECT_ID=dukefarm-production
OPSPULSE_INGEST_URL=https://ops.example.com/ingest/snapshots
OPSPULSE_COMMANDS_URL=https://ops.example.com/agent/commands
OPSPULSE_AGENT_TOKEN=<same token as GCP .env>
OPSPULSE_INTERVAL_MS=60000
OPSPULSE_COMMAND_POLL_MS=15000
OPSPULSE_COMMAND_TIMEOUT_MS=120000
DUKEFARM_BASE_URL=http://127.0.0.1:4000
DUKEFARM_BACKEND_DIR=/path/to/DukeFarm-Backend
DUKEFARM_BRANCH=main
```

## Manual test

```bash
set -a
source .env
set +a
node src/index.js
```

Stop after the first successful `sent snapshot` log.

## Start with PM2

```bash
cd /opt/opspulse-agent
pm2 start src/index.js --name opspulse-agent --time --update-env
pm2 save
```

## Smoke checks

```bash
pm2 status
curl http://127.0.0.1:4000/healthz
curl http://127.0.0.1:4000/api/v1/health
```

The OpsPulse dashboard should show the agent as live within one minute.

## Control-plane actions

The agent polls OpsPulse for allowlisted commands. It never accepts arbitrary shell text.

Supported actions:

- `health_check_now` checks `/healthz` and `/api/v1/health`.
- `pm2_restart_process` restarts only `dukefarm-backend`, `dukefarm-admin`, `dukefarm-frontend`, or `opspulse-agent`.
- `redeploy_backend` runs `git fetch`, `git reset --hard origin/$DUKEFARM_BRANCH`, `npm ci`, `npm run prisma:generate`, `npm run build`, `pm2 restart dukefarm-backend`, then health verification.
- `prisma_migrate_deploy` runs `npx prisma migrate deploy` as a separate explicit action.
- `rollback_backend` resets to the latest successful redeploy commit recorded by OpsPulse, rebuilds, restarts, then health-checks.

Before enabling redeploy/rollback, confirm:

```bash
cd "$DUKEFARM_BACKEND_DIR"
git status --short
npm ci
npm run prisma:generate
npm run build
pm2 status
```
