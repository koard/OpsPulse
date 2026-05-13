# OpsPulse DevOps Monitoring Platform

OpsPulse is a monitoring platform designed around a real university-hosted workload: Ubuntu, Nginx reverse proxy, PM2, and three running processes named `frontend`, `admin`, and `backend`.

The current MVP monitors one project well and keeps the architecture ready for more projects later.

## Architecture

- `apps/web` - Next.js dashboard for project health, PM2 process status, endpoint checks, alerts, and host metrics.
- `backend/src/Gateway.Api` - .NET BFF/API gateway that shapes data for the dashboard.
- `backend/src/ProjectRegistry.Api` - project inventory service for servers, endpoints, ownership, and stack metadata.
- `backend/src/Telemetry.Api` - telemetry ingest, history, incident, SLO, and safe command queue service.
- `backend/src/Alerting.Worker` - background worker that evaluates alert rules from the latest telemetry snapshot.
- `backend/src/Platform.Domain` - shared health policy, alert evaluation, and domain models.
- `agent/opspulse-agent` - Node.js PM2 agent that sends telemetry and pulls allowlisted DukeFarm control commands.

## Local Development

Frontend only:

```powershell
cd apps/web
npm install
npm run dev
```

The dashboard works without backend services by using realistic sample data.

Backend requires the .NET 8 SDK:

```powershell
cd backend
dotnet test tests/Platform.Domain.Tests/Platform.Domain.Tests.csproj
dotnet run --project src/Gateway.Api/Gateway.Api.csproj
```

Full stack with Docker:

```powershell
docker compose up --build
```

Docker Desktop needs to be running first. The dashboard is exposed at `http://localhost:3000`, and the gateway API is exposed at `http://localhost:5080`.

## Project Story

This project demonstrates:

- microservice decomposition with .NET APIs and a worker service
- dashboard/BFF composition for frontend-friendly contracts
- health scoring and alert rules covered by tests
- Dockerized local environment
- server-agent direction for Ubuntu + Nginx + PM2
- safe SRE control plane with command confirmation, allowlisted actions, and audit history
- architecture that starts with one production project and scales to many

## Next Iterations

- Production mode uses PostgreSQL through `DATABASE_URL`; local mode can still fall back to in-memory sample data.
- Add authentication and role-based access.
- Add a secure agent registration token per project.
- Add GitHub Actions for build, test, Docker image publish, and OpsPulse deployment.
- Add VPN-aware deployment documentation for the university server.

## Production

Cost-conscious production uses one GCP Compute Engine VM, Docker Compose, Caddy, and a Postgres container.

- GCP runbook: `docs/gcp-production-runbook.md`
- DukeFarm agent runbook: `docs/dukefarm-agent-runbook.md`
- Production compose: `docker-compose.prod.yml`
- Example env: `.env.production.example`
