# OpsPulse GCP Production Runbook

This runbook deploys OpsPulse on one cost-conscious Compute Engine VM with Docker Compose, Caddy, and a Postgres container on a persistent Docker volume.

## 1. VM shape

- Machine: `e2-small` minimum, `e2-medium` if memory is tight.
- OS: Ubuntu 22.04 LTS or 24.04 LTS.
- Disk: 30 GB standard persistent disk.
- Firewall: allow `80`, `443`, and restricted `22`.
- Do not expose `3000`, `5080`, `5081`, `5082`, or `5432`.

## 2. VM setup

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl git
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo tee /etc/apt/keyrings/docker.asc > /dev/null
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker "$USER"
```

Sign out and back in after adding the Docker group.

## 3. Deploy

```bash
sudo mkdir -p /opt/opspulse
sudo chown "$USER:$USER" /opt/opspulse
git clone <your-repo-url> /opt/opspulse
cd /opt/opspulse
cp .env.production.example .env
chmod 600 .env
```

Edit `.env`, then run:

```bash
docker build -t opspulse-project-registry:latest -f backend/src/ProjectRegistry.Api/Dockerfile backend
docker build -t opspulse-telemetry:latest -f backend/src/Telemetry.Api/Dockerfile backend
docker build -t opspulse-gateway:latest -f backend/src/Gateway.Api/Dockerfile backend
docker build -t opspulse-alerting:latest -f backend/src/Alerting.Worker/Dockerfile backend
docker build -t opspulse-web:latest apps/web
docker compose --env-file .env -f docker-compose.prod.yml up -d
```

Keep the agent `PROJECT_ID` and GCP `MONITORED_PROJECT_ID` aligned. The default
project id is `dukefarm`.

For GitHub deployments, also set:

```bash
GITHUB_TOKEN=<github-token-with-repo-read-access>
GITHUB_WEBHOOK_SECRET=<long-random-secret>
DUKEFARM_BACKEND_REPO=koard/DukeFarm-Backend
DUKEFARM_FRONTEND_REPO=koard/DukeFarm-Frontend
DUKEFARM_ADMIN_REPO=koard/DukeFarm-Admin
```

Create a GitHub webhook in each DukeFarm repo:

```text
Payload URL: https://$OPSPULSE_DOMAIN/github/webhook
Content type: application/json
Secret: same value as GITHUB_WEBHOOK_SECRET
Events: Push events only
```

Default repository settings start with auto deploy enabled for `main`. Disable
per service from the OpsPulse Deployments tab before adding webhooks if you only
want commit history at first.

## 4. Smoke checks

```bash
docker compose --env-file .env -f docker-compose.prod.yml ps
curl https://$OPSPULSE_DOMAIN
curl https://$OPSPULSE_DOMAIN/api/dashboard
curl https://$OPSPULSE_DOMAIN/api/commands?projectId=dukefarm
```

## 5. Backup

Install a daily cron job:

```bash
mkdir -p /opt/opspulse/backups
crontab -e
```

Add:

```cron
15 2 * * * cd /opt/opspulse && env $(cat .env | xargs) COMPOSE_FILE=/opt/opspulse/docker-compose.prod.yml /opt/opspulse/ops/scripts/backup-postgres.sh >> /opt/opspulse/backups/backup.log 2>&1
```

## 6. Restore drill

```bash
gunzip -c /opt/opspulse/backups/opspulse-YYYYMMDD-HHMMSS.sql.gz | docker compose --env-file .env -f docker-compose.prod.yml exec -T postgres psql -U "$POSTGRES_USER" "$POSTGRES_DB"
```

## 7. Control-plane smoke checks

Create a low-risk command from the dashboard first:

- Open `https://$OPSPULSE_DOMAIN`.
- Go to `Actions`.
- Select `Health Check Now`.
- Type `dukefarm` to confirm.
- Queue the action.

The DukeFarm agent should claim it within `OPSPULSE_COMMAND_POLL_MS`, run endpoint checks, and write the result back to the deployment history. Use redeploy, migrate, rollback, and PM2 restart only after the health-check command works.
