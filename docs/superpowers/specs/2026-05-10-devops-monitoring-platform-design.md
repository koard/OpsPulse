# DevOps Monitoring Platform Design

## Product

OpsPulse is a DevOps monitoring platform for a small but realistic microservice estate. It starts with one university-hosted project and is shaped so more projects can be onboarded later.

## Architecture

The platform uses Next.js for the dashboard and .NET 8 for microservices:

- Gateway API composes dashboard data.
- Project Registry API stores project/server/endpoint inventory.
- Telemetry API ingests and returns process, host, and endpoint snapshots.
- Alerting Worker evaluates telemetry on a schedule.
- Platform Domain contains shared health and alert policy.

## First Project Model

The seed project represents the current server:

- Ubuntu server
- VPN-only access
- Nginx reverse proxy
- PM2 process manager
- processes: `frontend`, `admin`, `backend`

## Dashboard

The dashboard shows project health, health score, PM2 process table, endpoint checks, host resource meters, alert list, and recent timeline. It uses sample data when the backend is not running, so the demo remains usable.

## Testing

Frontend helper logic is tested with Vitest. Backend domain policy is covered by xUnit tests. The local machine currently has Node installed, but no .NET SDK, so backend tests are ready but require .NET 8 SDK or Docker.

## Future Growth

Next steps are PostgreSQL/TimescaleDB persistence, authenticated project agents, GitHub Actions CI, deployment scripts, and notification channels.
