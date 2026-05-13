"use client";

import {
  Activity,
  AlertTriangle,
  Bell,
  CheckCircle2,
  CircleGauge,
  ClipboardCheck,
  Clock3,
  Cpu,
  DatabaseZap,
  ExternalLink,
  Gauge,
  GitBranch,
  Globe2,
  HardDrive,
  Network,
  RefreshCcw,
  Search,
  Server,
  ShieldCheck,
  TerminalSquare,
} from "lucide-react";
import type { CSSProperties, ReactNode } from "react";
import { useMemo, useState } from "react";
import {
  buildDashboardSummary,
  filterProcesses,
  formatUptime,
  getHealthScore,
  getOpenIncidents,
  getStaleAgents,
  getProjectSeverity,
  summarizeSlo,
} from "@/lib/dashboard";
import type {
  AlertSeverity,
  AgentStatus,
  CreateCommandInput,
  HostMetrics,
  Incident,
  OpsCommand,
  ProcessMetric,
  Severity,
  SloReport,
  SrePayload,
  TimelinePoint,
} from "@/lib/types";

type DashboardShellProps = {
  payload: SrePayload;
};

type DashboardTab = "overview" | "incidents" | "slo" | "agents" | "actions" | "runbook";

const severityLabel: Record<Severity, string> = {
  healthy: "Healthy",
  warning: "Watch",
  critical: "Incident",
};

const severityClass: Record<Severity, string> = {
  healthy: "severityHealthy",
  warning: "severityWarning",
  critical: "severityCritical",
};

const alertClass: Record<AlertSeverity, string> = {
  info: "alertInfo",
  warning: "alertWarning",
  critical: "alertCritical",
};

export function DashboardShell({ payload }: DashboardShellProps) {
  const dashboard = payload.dashboard;
  const [activeTab, setActiveTab] = useState<DashboardTab>("overview");
  const [incidents, setIncidents] = useState(payload.incidents);
  const [commands, setCommands] = useState(payload.commands);
  const [selectedProjectId, setSelectedProjectId] = useState(
    dashboard.projects[0]?.id ?? "",
  );
  const [processSearch, setProcessSearch] = useState("");

  const summary = useMemo(() => buildDashboardSummary(dashboard), [dashboard]);
  const selectedProject =
    dashboard.projects.find((project) => project.id === selectedProjectId) ??
    dashboard.projects[0];
  const visibleProcesses = useMemo(
    () => filterProcesses(selectedProject?.processes ?? [], processSearch),
    [processSearch, selectedProject],
  );
  const openIncidents = useMemo(
    () => getOpenIncidents(incidents),
    [incidents],
  );
  const staleAgents = useMemo(
    () => getStaleAgents(payload.agents),
    [payload.agents],
  );

  if (!selectedProject) {
    return (
      <main className="emptyShell">
        <div className="emptyState">
          <Server aria-hidden="true" />
          <h1>No projects connected</h1>
        </div>
      </main>
    );
  }

  const projectSeverity = getProjectSeverity(selectedProject);
  const healthScore = getHealthScore(selectedProject);
  const healthRingStyle = { "--score": healthScore } as CSSProperties;

  return (
    <main className="dashboardShell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brandMark">
            <Activity aria-hidden="true" />
          </div>
          <div>
            <span className="brandName">OpsPulse</span>
            <span className="brandMeta">DevOps monitoring</span>
          </div>
        </div>

        <nav className="projectNav" aria-label="Projects">
          {dashboard.projects.map((project) => {
            const severity = getProjectSeverity(project);
            return (
              <button
                className={`projectNavItem ${selectedProject.id === project.id ? "isActive" : ""}`}
                key={project.id}
                onClick={() => setSelectedProjectId(project.id)}
                type="button"
              >
                <span className={`projectDot ${severityClass[severity]}`} />
                <span>
                  <strong>{project.name}</strong>
                  <small>{project.environment}</small>
                </span>
              </button>
            );
          })}
        </nav>

        <div className="sidebarFooter">
          <ShieldCheck aria-hidden="true" />
          <span>{selectedProject.server.access}</span>
        </div>
      </aside>

      <section className="mainStage">
        <header className="topbar">
          <div>
            <h1>{selectedProject.name}</h1>
            <p>
              {selectedProject.server.hostname} · {selectedProject.server.os}
            </p>
          </div>
          <div className="topbarActions">
            <span className={`statusPill ${severityClass[projectSeverity]}`}>
              {severityLabel[projectSeverity]}
            </span>
            <button className="iconButton" type="button" aria-label="Refresh">
              <RefreshCcw aria-hidden="true" />
            </button>
          </div>
        </header>

        <section className="summaryGrid" aria-label="Platform summary">
          <MetricTile
            icon={<GitBranch aria-hidden="true" />}
            label="Projects"
            value={summary.totalProjects}
            detail="managed estate"
          />
          <MetricTile
            icon={<TerminalSquare aria-hidden="true" />}
            label="Online processes"
            value={summary.onlineProcesses}
            detail={`${selectedProject.processes.length} tracked here`}
          />
          <MetricTile
            icon={<CircleGauge aria-hidden="true" />}
            label="Health score"
            value={healthScore}
            detail="latest collection"
          />
          <MetricTile
            icon={<Bell aria-hidden="true" />}
            label="Open incidents"
            value={openIncidents.length}
            detail={`${summary.openCriticalAlerts} critical`}
          />
          <MetricTile
            icon={<ClipboardCheck aria-hidden="true" />}
            label="Stale agents"
            value={staleAgents.length}
            detail="configuration drift"
          />
        </section>

        <nav className="tabs" aria-label="SRE dashboard sections">
          {[
            ["overview", "Overview"],
            ["incidents", "Incidents"],
            ["slo", "SLO"],
            ["agents", "Agents"],
            ["actions", "Actions"],
            ["runbook", "Runbook"],
          ].map(([tab, label]) => (
            <button
              className={activeTab === tab ? "isActive" : ""}
              key={tab}
              onClick={() => setActiveTab(tab as DashboardTab)}
              type="button"
            >
              {label}
            </button>
          ))}
        </nav>

        {activeTab === "overview" && (
          <section className="workGrid">
          <div className="primaryColumn">
            <section className="panel heroPanel">
              <div className="healthRing" style={healthRingStyle}>
                <span>{healthScore}</span>
              </div>
              <div className="heroCopy">
                <span className="sectionLabel">Live posture</span>
                <h2>{selectedProject.server.processManager} process health</h2>
                <p>
                  Nginx routes traffic to frontend, admin, and backend services.
                  VPN-restricted checks keep the public surface small while
                  preserving operational visibility.
                </p>
                <div className="stackBadges" aria-label="Runtime stack">
                  <span>Nginx reverse proxy</span>
                  <span>PM2</span>
                  <span>Ubuntu</span>
                  <span>.NET microservices</span>
                </div>
              </div>
            </section>

            <section className="panel">
              <div className="panelHeader">
                <div>
                  <span className="sectionLabel">Processes</span>
                  <h2>PM2 inventory</h2>
                </div>
                <label className="searchBox">
                  <Search aria-hidden="true" />
                  <input
                    aria-label="Search processes"
                    onChange={(event) => setProcessSearch(event.target.value)}
                    placeholder="Search"
                    value={processSearch}
                  />
                </label>
              </div>
              <div className="processTable" role="table">
                <div className="processRow processHead" role="row">
                  <span>Process</span>
                  <span>Status</span>
                  <span>CPU</span>
                  <span>Memory</span>
                  <span>Uptime</span>
                </div>
                {visibleProcesses.map((process) => (
                  <ProcessRow key={process.name} process={process} />
                ))}
              </div>
            </section>

            <section className="panel">
              <div className="panelHeader">
                <div>
                  <span className="sectionLabel">Network edge</span>
                  <h2>Endpoint checks</h2>
                </div>
                <Globe2 aria-hidden="true" className="panelIcon" />
              </div>
              <div className="endpointList">
                {selectedProject.endpoints.map((endpoint) => (
                  <a
                    className="endpointItem"
                    href={endpoint.url}
                    key={endpoint.name}
                    rel="noreferrer"
                    target="_blank"
                  >
                    <span>
                      <strong>{endpoint.name}</strong>
                      <small>{endpoint.url}</small>
                    </span>
                    <span className="endpointMeta">
                      {endpoint.statusCode}
                      <small>{endpoint.latencyMs} ms</small>
                    </span>
                    <ExternalLink aria-hidden="true" />
                  </a>
                ))}
              </div>
            </section>
          </div>

          <div className="sideColumn">
            <section className="panel">
              <div className="panelHeader">
                <div>
                  <span className="sectionLabel">Host metrics</span>
                  <h2>Ubuntu server</h2>
                </div>
                <Server aria-hidden="true" className="panelIcon" />
              </div>
              <ResourceMeter
                icon={<Cpu aria-hidden="true" />}
                label="CPU"
                value={selectedProject.metrics.cpuPercent}
              />
              <ResourceMeter
                icon={<DatabaseZap aria-hidden="true" />}
                label="Memory"
                value={selectedProject.metrics.memoryPercent}
              />
              <ResourceMeter
                icon={<HardDrive aria-hidden="true" />}
                label="Disk"
                value={selectedProject.metrics.diskPercent}
              />
              <NetworkMeter metrics={selectedProject.metrics} />
            </section>

            <section className="panel">
              <div className="panelHeader">
                <div>
                  <span className="sectionLabel">Timeline</span>
                  <h2>Last window</h2>
                </div>
                <Gauge aria-hidden="true" className="panelIcon" />
              </div>
              <Sparkline points={selectedProject.timeline} />
            </section>

            <section className="panel">
              <div className="panelHeader">
                <div>
                  <span className="sectionLabel">Alerts</span>
                  <h2>Open signals</h2>
                </div>
                <AlertTriangle aria-hidden="true" className="panelIcon" />
              </div>
              <div className="alertList">
                {selectedProject.alerts.map((alert) => (
                  <article
                    className={`alertItem ${alertClass[alert.severity]}`}
                    key={alert.id}
                  >
                    <div>
                      <strong>{alert.title}</strong>
                      <p>{alert.message}</p>
                    </div>
                    <Clock3 aria-hidden="true" />
                  </article>
                ))}
              </div>
            </section>
          </div>
          </section>
        )}

        {activeTab === "incidents" && (
          <IncidentCenter incidents={incidents} onIncidentChange={(incident) => {
            setIncidents((current) => current.map((item) => item.id === incident.id ? incident : item));
          }} />
        )}

        {activeTab === "slo" && (
          <SloView report={payload.slo} />
        )}

        {activeTab === "agents" && (
          <AgentInventory agents={payload.agents} />
        )}

        {activeTab === "actions" && (
          <ActionsView
            commands={commands}
            projectId={selectedProject.id}
            onCommandCreated={(command) => {
              setCommands((current) => [command, ...current]);
            }}
          />
        )}

        {activeTab === "runbook" && (
          <RunbookView />
        )}
      </section>
    </main>
  );
}

function MetricTile({
  detail,
  icon,
  label,
  value,
}: {
  detail: string;
  icon: ReactNode;
  label: string;
  value: number;
}) {
  return (
    <article className="metricTile">
      <div className="metricIcon">{icon}</div>
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
    </article>
  );
}

function ProcessRow({ process }: { process: ProcessMetric }) {
  const isOnline = process.status === "online";

  return (
    <div className="processRow" role="row">
      <span>
        <strong>{process.name}</strong>
        <small>{process.role}</small>
      </span>
      <span className={`processStatus ${isOnline ? "isOnline" : "isDown"}`}>
        {isOnline ? (
          <CheckCircle2 aria-hidden="true" />
        ) : (
          <AlertTriangle aria-hidden="true" />
        )}
        {process.status}
      </span>
      <span>{process.cpuPercent}%</span>
      <span>{process.memoryMb} MB</span>
      <span>{formatUptime(process.uptimeSeconds)}</span>
    </div>
  );
}

function ResourceMeter({
  icon,
  label,
  value,
}: {
  icon: ReactNode;
  label: string;
  value: number;
}) {
  return (
    <div className="resourceMeter">
      <div className="meterLabel">
        <span>
          {icon}
          {label}
        </span>
        <strong>{value}%</strong>
      </div>
      <div className="meterTrack">
        <span style={{ width: `${value}%` }} />
      </div>
    </div>
  );
}

function NetworkMeter({ metrics }: { metrics: HostMetrics }) {
  return (
    <div className="networkMeter">
      <Network aria-hidden="true" />
      <span>
        <strong>{metrics.networkInMbps.toFixed(1)} Mbps</strong>
        inbound
      </span>
      <span>
        <strong>{metrics.networkOutMbps.toFixed(1)} Mbps</strong>
        outbound
      </span>
    </div>
  );
}

function Sparkline({ points }: { points: TimelinePoint[] }) {
  const width = 260;
  const height = 96;
  const maxLatency = Math.max(...points.map((point) => point.latencyMs), 1);
  const scorePath = points
    .map((point, index) => {
      const x = (index / Math.max(points.length - 1, 1)) * width;
      const y = height - (point.healthScore / 100) * height;
      return `${index === 0 ? "M" : "L"} ${x.toFixed(1)} ${y.toFixed(1)}`;
    })
    .join(" ");
  const latencyPath = points
    .map((point, index) => {
      const x = (index / Math.max(points.length - 1, 1)) * width;
      const y = height - (point.latencyMs / maxLatency) * height;
      return `${index === 0 ? "M" : "L"} ${x.toFixed(1)} ${y.toFixed(1)}`;
    })
    .join(" ");

  return (
    <div className="sparkline">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Health trend">
        <path d={latencyPath} className="latencyLine" />
        <path d={scorePath} className="scoreLine" />
      </svg>
      <div className="timelineLabels">
        {points.map((point) => (
          <span key={point.at}>{point.at}</span>
        ))}
      </div>
    </div>
  );
}

function IncidentCenter({
  incidents,
  onIncidentChange,
}: {
  incidents: Incident[];
  onIncidentChange: (incident: Incident) => void;
}) {
  async function updateIncident(id: string, action: "acknowledge" | "resolve") {
    const response = await fetch(`/api/incidents/${id}/${action}`, { method: "POST" });
    if (!response.ok) {
      return;
    }

    onIncidentChange((await response.json()) as Incident);
  }

  return (
    <section className="panel">
      <div className="panelHeader">
        <div>
          <span className="sectionLabel">Incident center</span>
          <h2>Grouped operational events</h2>
        </div>
        <Bell aria-hidden="true" className="panelIcon" />
      </div>
      <div className="incidentTable">
        <div className="incidentRow incidentHead">
          <span>Incident</span>
          <span>Status</span>
          <span>Severity</span>
          <span>Occurrences</span>
          <span>Root cause hint</span>
          <span>Actions</span>
        </div>
        {incidents.map((incident) => (
          <article className="incidentRow" key={incident.id}>
            <span>
              <strong>{incident.title}</strong>
              <small>{incident.summary}</small>
            </span>
            <span className="incidentStatus">{incident.status}</span>
            <span className={`statusPill ${alertClass[incident.severity]}`}>
              {incident.severity}
            </span>
            <span>{incident.occurrences}</span>
            <span>{incident.rootCauseHint}</span>
            <span className="incidentActions">
              <button
                disabled={incident.status === "resolved"}
                onClick={() => updateIncident(incident.id, "acknowledge")}
                type="button"
              >
                Ack
              </button>
              <button
                disabled={incident.status === "resolved"}
                onClick={() => updateIncident(incident.id, "resolve")}
                type="button"
              >
                Resolve
              </button>
            </span>
          </article>
        ))}
      </div>
    </section>
  );
}

function SloView({ report }: { report?: SloReport | null }) {
  if (!report) {
    return (
      <section className="panel">
        <span className="sectionLabel">SLO</span>
        <h2>No SLO data yet</h2>
      </section>
    );
  }

  return (
    <section className="workGrid">
      <div className="panel heroPanel">
        <div className="healthRing" style={{ "--score": report.availabilityPercent } as CSSProperties}>
          <span>{report.availabilityPercent.toFixed(1)}</span>
        </div>
        <div className="heroCopy">
          <span className="sectionLabel">Service level objective</span>
          <h2>{summarizeSlo(report)}</h2>
          <p>
            Target availability is {report.targetAvailabilityPercent.toFixed(2)}%.
            Error budget burn helps decide whether to ship, pause, or investigate.
          </p>
        </div>
      </div>
      <div className="panel">
        <div className="panelHeader">
          <div>
            <span className="sectionLabel">Endpoint SLO</span>
            <h2>Availability and latency</h2>
          </div>
          <Gauge aria-hidden="true" className="panelIcon" />
        </div>
        <div className="endpointList">
          {report.endpoints.map((endpoint) => (
            <div className="endpointItem" key={`${endpoint.name}-${endpoint.url}`}>
              <span>
                <strong>{endpoint.name}</strong>
                <small>{endpoint.totalChecks} checks · {endpoint.failedChecks} failures</small>
              </span>
              <span className="endpointMeta">
                {endpoint.availabilityPercent.toFixed(2)}%
                <small>p95 {endpoint.p95LatencyMs} ms · p99 {endpoint.p99LatencyMs} ms</small>
              </span>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

function AgentInventory({ agents }: { agents: AgentStatus[] }) {
  return (
    <section className="panel">
      <div className="panelHeader">
        <div>
          <span className="sectionLabel">Agent inventory</span>
          <h2>Server sender health and drift</h2>
        </div>
        <ShieldCheck aria-hidden="true" className="panelIcon" />
      </div>
      <div className="agentGrid">
        {agents.map((agent) => (
          <article className="agentCard" key={agent.projectId}>
            <div>
              <strong>{agent.agent?.hostname ?? agent.projectId}</strong>
              <small>{agent.agent?.os ?? "unknown os"} · PM2 {agent.agent?.pm2Version ?? "unknown"}</small>
            </div>
            <span className={`statusPill ${agent.isStale ? "severityCritical" : "severityHealthy"}`}>
              {agent.isStale ? "stale" : "live"}
            </span>
            <div className="driftList">
              {agent.driftIssues.length === 0 ? (
                <small>No drift detected</small>
              ) : (
                agent.driftIssues.map((issue) => (
                  <small key={`${agent.projectId}-${issue.kind}-${issue.message}`}>{issue.message}</small>
                ))
              )}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

const commandActions = [
  { value: "health_check_now", label: "Health Check Now", confirmation: "project" },
  { value: "pm2_restart_process", label: "Restart PM2 Process", confirmation: "target" },
  { value: "redeploy_backend", label: "Redeploy Backend", confirmation: "project" },
  { value: "prisma_migrate_deploy", label: "Run Prisma Migrate Deploy", confirmation: "project" },
  { value: "rollback_backend", label: "Rollback Last Release", confirmation: "project" },
] as const;

const processTargets = [
  "dukefarm-backend",
  "dukefarm-admin",
  "dukefarm-frontend",
  "opspulse-agent",
];

const commandActionLabel: Record<string, string> = {
  healthCheckNow: "Health Check Now",
  pm2RestartProcess: "Restart PM2 Process",
  redeployBackend: "Redeploy Backend",
  prismaMigrateDeploy: "Prisma Migrate Deploy",
  rollbackBackend: "Rollback",
};

function ActionsView({
  commands,
  projectId,
  onCommandCreated,
}: {
  commands: OpsCommand[];
  projectId: string;
  onCommandCreated: (command: OpsCommand) => void;
}) {
  const [action, setAction] = useState<(typeof commandActions)[number]["value"]>("health_check_now");
  const [target, setTarget] = useState(processTargets[0]);
  const [confirmation, setConfirmation] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const selectedAction = commandActions.find((item) => item.value === action) ?? commandActions[0];
  const expectedConfirmation = selectedAction.confirmation === "target" ? target : projectId;
  const canSubmit = confirmation === expectedConfirmation;

  async function createCommand() {
    const payload: CreateCommandInput = {
      projectId,
      action,
      target: action === "pm2_restart_process" ? target : null,
      requestedBy: "portfolio-user",
      confirmation,
    };
    const response = await fetch("/api/commands", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: "Command request failed." }));
      setMessage(error.error ?? "Command request failed.");
      return;
    }

    const command = (await response.json()) as OpsCommand;
    onCommandCreated(command);
    setConfirmation("");
    setMessage(`${selectedAction.label} queued.`);
  }

  return (
    <section className="workGrid">
      <div className="panel">
        <div className="panelHeader">
          <div>
            <span className="sectionLabel">Control plane</span>
            <h2>DukeFarm safe actions</h2>
          </div>
          <TerminalSquare aria-hidden="true" className="panelIcon" />
        </div>
        <div className="actionForm">
          <label>
            <span>Action</span>
            <select value={action} onChange={(event) => setAction(event.target.value as typeof action)}>
              {commandActions.map((item) => (
                <option key={item.value} value={item.value}>
                  {item.label}
                </option>
              ))}
            </select>
          </label>
          {action === "pm2_restart_process" && (
            <label>
              <span>PM2 process</span>
              <select value={target} onChange={(event) => setTarget(event.target.value)}>
                {processTargets.map((process) => (
                  <option key={process} value={process}>
                    {process}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label>
            <span>Type to confirm</span>
            <input
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              placeholder={expectedConfirmation}
            />
          </label>
          <button disabled={!canSubmit} onClick={createCommand} type="button">
            Queue action
          </button>
          {message && <small className="formMessage">{message}</small>}
        </div>
      </div>

      <div className="panel">
        <div className="panelHeader">
          <div>
            <span className="sectionLabel">Deployment history</span>
            <h2>Command audit trail</h2>
          </div>
          <GitBranch aria-hidden="true" className="panelIcon" />
        </div>
        <div className="commandList">
          {commands.length === 0 ? (
            <small>No commands queued yet</small>
          ) : (
            commands.map((command) => (
              <article className="commandItem" key={command.id}>
                <div>
                  <strong>{commandActionLabel[command.action] ?? command.action}</strong>
                  <small>{command.target} · requested by {command.requestedBy}</small>
                  {command.summary && <small>{command.summary}</small>}
                </div>
                <span className={`statusPill ${commandStatusClass(command.status)}`}>
                  {command.status}
                </span>
              </article>
            ))
          )}
        </div>
      </div>
    </section>
  );
}

function commandStatusClass(status: string) {
  if (status === "succeeded") {
    return "severityHealthy";
  }

  if (status === "failed" || status === "timedOut" || status === "cancelled") {
    return "severityCritical";
  }

  return "severityWarning";
}

function RunbookView() {
  return (
    <section className="panel runbook">
      <div className="panelHeader">
        <div>
          <span className="sectionLabel">Runbook</span>
          <h2>DukeFarm incident response</h2>
        </div>
        <ClipboardCheck aria-hidden="true" className="panelIcon" />
      </div>
      <ol>
        <li>Check PM2 process state for dukefarm-backend, admin, and frontend.</li>
        <li>Confirm DukeFarm API health at /healthz and /api/v1/health.</li>
        <li>Inspect memory pressure and restart count before restarting services.</li>
        <li>Resolve incident only after endpoint checks are healthy for two agent windows.</li>
      </ol>
    </section>
  );
}
