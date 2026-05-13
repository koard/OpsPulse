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
  RepositoryDeploymentView,
  Severity,
  SloReport,
  SrePayload,
  TimelinePoint,
} from "@/lib/types";

type DashboardShellProps = {
  payload: SrePayload;
};

type DashboardTab = "overview" | "incidents" | "slo" | "agents" | "actions" | "deployments" | "runbook";

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
  const [repositories, setRepositories] = useState(payload.repositories);
  const [selectedProjectId, setSelectedProjectId] = useState(
    dashboard.projects[0]?.id ?? "",
  );
  const [processSearch, setProcessSearch] = useState("");
  const [isRefreshing, setIsRefreshing] = useState(false);

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
            <button
              className={`iconButton ${isRefreshing ? "isLoading" : ""}`}
              type="button"
              aria-label="Refresh"
              onClick={() => {
                setIsRefreshing(true);
                setTimeout(() => setIsRefreshing(false), 800); // Simulate refresh
              }}
            >
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
            intent="info"
          />
          <MetricTile
            icon={<TerminalSquare aria-hidden="true" />}
            label="Online processes"
            value={summary.onlineProcesses}
            detail={`${selectedProject.processes.length} tracked here`}
            intent={summary.onlineProcesses === selectedProject.processes.length ? "success" : "warn"}
          />
          <MetricTile
            icon={<CircleGauge aria-hidden="true" />}
            label="Health score"
            value={healthScore}
            detail="latest collection"
            intent={healthScore >= 95 ? "success" : healthScore >= 80 ? "warn" : "critical"}
          />
          <MetricTile
            icon={<Bell aria-hidden="true" />}
            label="Open incidents"
            value={openIncidents.length}
            detail={`${summary.openCriticalAlerts} critical`}
            intent={openIncidents.length === 0 ? "success" : summary.openCriticalAlerts > 0 ? "critical" : "warn"}
          />
          <MetricTile
            icon={<ClipboardCheck aria-hidden="true" />}
            label="Stale agents"
            value={staleAgents.length}
            detail="configuration drift"
            intent={staleAgents.length === 0 ? "success" : "critical"}
          />
        </section>

        <nav className="tabs" aria-label="SRE dashboard sections">
          {[
            { id: "overview", label: "Overview", icon: <Activity aria-hidden="true" /> },
            { id: "incidents", label: "Incidents", icon: <AlertTriangle aria-hidden="true" />, count: openIncidents.length },
            { id: "slo", label: "SLO", icon: <CheckCircle2 aria-hidden="true" /> },
            { id: "agents", label: "Agents", icon: <Server aria-hidden="true" />, count: staleAgents.length },
            { id: "actions", label: "Actions", icon: <TerminalSquare aria-hidden="true" /> },
            { id: "deployments", label: "Deployments", icon: <GitBranch aria-hidden="true" /> },
            { id: "runbook", label: "Runbook", icon: <ClipboardCheck aria-hidden="true" /> },
          ].map((tab) => (
            <button
              className={activeTab === tab.id ? "isActive" : ""}
              key={tab.id}
              onClick={() => setActiveTab(tab.id as DashboardTab)}
              type="button"
            >
              {tab.icon}
              {tab.label}
              {tab.count !== undefined && tab.count > 0 && <span className="tabBadge">{tab.count}</span>}
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

        {activeTab === "deployments" && (
          <DeploymentsView
            projectId={selectedProject.id}
            repositories={repositories}
            onRepositoriesChange={setRepositories}
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
  intent = "info",
}: {
  detail: string;
  icon: ReactNode;
  label: string;
  value: number;
  intent?: "info" | "success" | "warn" | "critical";
}) {
  return (
    <article className="metricTile" data-intent={intent}>
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
  const cpuColor = process.cpuPercent > 80 ? "var(--color-danger)" : process.cpuPercent > 50 ? "var(--color-warn)" : "var(--color-info)";
  const memoryFill = Math.min((process.memoryMb / 1024) * 100, 100);

  return (
    <div className="processRow" role="row">
      <span>
        <strong>{process.name}</strong>
        <small>{process.role}</small>
      </span>
      <span className={`processStatus ${isOnline ? "isOnline" : "isDown"}`}>
        <span className="statusDot" />
        {process.status}
      </span>
      <div className="miniBar">
        <span>{process.cpuPercent}%</span>
        <div className="miniBarTrack">
          <div className="miniBarFill" style={{ width: `${process.cpuPercent}%`, backgroundColor: cpuColor }} />
        </div>
      </div>
      <div className="miniBar">
        <span>{process.memoryMb} MB</span>
        <div className="miniBarTrack">
          <div className="miniBarFill" style={{ width: `${memoryFill}%`, backgroundColor: "var(--color-success)" }} />
        </div>
      </div>
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
  if (points.length < 2) {
    return (
      <div className="sparkline">
        <div className="sparklineEmpty">Not enough data yet</div>
      </div>
    );
  }

  const W = 600;
  const H = 120;
  const PAD = { top: 12, right: 8, bottom: 4, left: 8 };
  const chartW = W - PAD.left - PAD.right;
  const chartH = H - PAD.top - PAD.bottom;

  const maxLatency = Math.max(...points.map((p) => p.latencyMs), 1);
  const n = points.length;

  // Generate a smooth cubic-bezier path
  function smoothPath(getValue: (p: typeof points[0]) => number, flip = true) {
    const coords = points.map((p, i) => ({
      x: PAD.left + (i / (n - 1)) * chartW,
      y: PAD.top + (flip ? 1 - getValue(p) : getValue(p)) * chartH,
    }));
    return coords
      .map((pt, i) => {
        if (i === 0) return `M ${pt.x.toFixed(1)} ${pt.y.toFixed(1)}`;
        const prev = coords[i - 1];
        const cpx = (prev.x + pt.x) / 2;
        return `C ${cpx.toFixed(1)} ${prev.y.toFixed(1)}, ${cpx.toFixed(1)} ${pt.y.toFixed(1)}, ${pt.x.toFixed(1)} ${pt.y.toFixed(1)}`;
      })
      .join(" ");
  }

  const scoreLine = smoothPath((p) => p.healthScore / 100);
  const latencyLine = smoothPath((p) => p.latencyMs / maxLatency);

  // Area fill path for score — close back down to bottom
  const areaClose = ` L ${(PAD.left + chartW).toFixed(1)} ${(PAD.top + chartH).toFixed(1)} L ${PAD.left.toFixed(1)} ${(PAD.top + chartH).toFixed(1)} Z`;
  const scoreArea = scoreLine + areaClose;

  // Grid lines (3 horizontal)
  const gridYs = [0.25, 0.5, 0.75].map((r) => PAD.top + r * chartH);

  // X-axis labels — pick at most 5 evenly spaced
  const labelCount = Math.min(5, n);
  const labelIndices = Array.from({ length: labelCount }, (_, i) =>
    Math.round((i / (labelCount - 1)) * (n - 1))
  );

  return (
    <div className="sparkline">
      <svg
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label="Health and latency trend"
        className="sparklineSvg"
      >
        <defs>
          <linearGradient id="scoreGrad" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--accent)" stopOpacity="0.25" />
            <stop offset="100%" stopColor="var(--accent)" stopOpacity="0" />
          </linearGradient>
        </defs>

        {/* Grid lines */}
        {gridYs.map((y) => (
          <line
            key={y}
            x1={PAD.left}
            x2={PAD.left + chartW}
            y1={y}
            y2={y}
            className="sparklineGrid"
          />
        ))}

        {/* Area fill */}
        <path d={scoreArea} fill="url(#scoreGrad)" />

        {/* Latency dashed line */}
        <path d={latencyLine} className="latencyLine" />

        {/* Score solid line */}
        <path d={scoreLine} className="scoreLine" />

        {/* End dot on score line */}
        {(() => {
          const last = points[n - 1];
          const x = PAD.left + chartW;
          const y = PAD.top + (1 - last.healthScore / 100) * chartH;
          return (
            <circle cx={x} cy={y} r="4" className="scoreEndDot" />
          );
        })()}
      </svg>

      {/* X-axis labels */}
      <div className="sparklineAxis">
        {labelIndices.map((idx) => (
          <span
            key={points[idx].at}
            style={{ left: `${(idx / (n - 1)) * 100}%` }}
          >
            {points[idx].at}
          </span>
        ))}
      </div>

      {/* Legend */}
      <div className="sparklineLegend">
        <span className="legendScore">Health score</span>
        <span className="legendLatency">Latency</span>
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
      <div className="panel sloHero">
        <div className="sloRingContainer">
          <svg viewBox="0 0 100 100" className="sloRingSvg">
            <circle cx="50" cy="50" r="45" className="sloRingBg" />
            <circle
              cx="50"
              cy="50"
              r="45"
              className="sloRingProgress"
              style={{ strokeDashoffset: `calc(283 - (283 * ${report.availabilityPercent}) / 100)` }}
            />
          </svg>
          <div className="sloRingText">
            <span className="sloRingValue">{report.availabilityPercent.toFixed(1)}</span>
            <span className="sloRingPercent">%</span>
          </div>
        </div>
        <div className="heroCopy">
          <span className="sectionLabel">Service level objective</span>
          <h2>{summarizeSlo(report)}</h2>
          <p>
            Target availability is <strong>{report.targetAvailabilityPercent.toFixed(2)}%</strong>.
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
          {report.endpoints.map((endpoint) => {
            const isHealthy = endpoint.availabilityPercent >= report.targetAvailabilityPercent;
            return (
              <div className={`endpointCard ${isHealthy ? "isHealthy" : "isFailing"}`} key={`${endpoint.name}-${endpoint.url}`}>
                <div className="endpointHeader">
                  <strong>{endpoint.name}</strong>
                  <span className="availBadge">{endpoint.availabilityPercent.toFixed(2)}%</span>
                </div>
                <div className="endpointStats">
                  <div className="endpointCheckInfo">
                    <span><Activity size={12} aria-hidden="true"/> {endpoint.totalChecks} checks</span>
                    {endpoint.failedChecks > 0 ? (
                      <span className="failCount"><AlertTriangle size={12} aria-hidden="true"/> {endpoint.failedChecks} failures</span>
                    ) : (
                      <span>0 failures</span>
                    )}
                  </div>
                  <div className="endpointLatency">
                    <span>p95 <strong>{endpoint.p95LatencyMs}</strong>ms</span>
                    <span>p99 <strong>{endpoint.p99LatencyMs}</strong>ms</span>
                  </div>
                </div>
              </div>
            );
          })}
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
          <article className={`agentCard ${agent.isStale ? "isStale" : ""}`} key={agent.projectId}>
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
                agent.driftIssues.map((issue) => {
                  // Replace raw UTC dates in the message with Thai locale time
                  const formattedMessage = issue.message.replace(
                    /\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?Z?/,
                    (match) => formatThaiTime(match.replace(" ", "T"))
                  );
                  return (
                    <small key={`${agent.projectId}-${issue.kind}-${issue.message}`}>
                      {formattedMessage}
                    </small>
                  );
                })
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
  { value: "redeploy_frontend", label: "Redeploy Frontend", confirmation: "project" },
  { value: "redeploy_admin", label: "Redeploy Admin", confirmation: "project" },
  { value: "prisma_migrate_deploy", label: "Run Prisma Migrate Deploy", confirmation: "project" },
  { value: "rollback_backend", label: "Rollback Backend", confirmation: "project" },
  { value: "rollback_frontend", label: "Rollback Frontend", confirmation: "project" },
  { value: "rollback_admin", label: "Rollback Admin", confirmation: "project" },
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
  redeployFrontend: "Redeploy Frontend",
  redeployAdmin: "Redeploy Admin",
  prismaMigrateDeploy: "Prisma Migrate Deploy",
  rollbackBackend: "Rollback Backend",
  rollbackFrontend: "Rollback Frontend",
  rollbackAdmin: "Rollback Admin",
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
      requestedBy: "dashboard-user",
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
          <div className="actionSelector">
            {commandActions.map((item) => (
              <label key={item.value} className="actionOption">
                <input
                  type="radio"
                  name="action"
                  value={item.value}
                  checked={action === item.value}
                  onChange={(event) => setAction(event.target.value as typeof action)}
                />
                <strong>{item.label}</strong>
                <span>{item.confirmation === "project" ? "Whole project" : "Specific process"}</span>
              </label>
            ))}
          </div>
          {action === "pm2_restart_process" && (
            <label className="inputLabel">
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
          <label className="inputLabel">
            <span>Type '{expectedConfirmation}' to confirm</span>
            <input
              type="text"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              placeholder={expectedConfirmation}
            />
          </label>
          <button disabled={!canSubmit} onClick={createCommand} type="button">
            Queue action
          </button>
          {message && <small className="formMessage"><CheckCircle2 aria-hidden="true" size={14}/> {message}</small>}
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

function DeploymentsView({
  projectId,
  repositories,
  onRepositoriesChange,
  onCommandCreated,
}: {
  projectId: string;
  repositories: RepositoryDeploymentView[];
  onRepositoriesChange: (repositories: RepositoryDeploymentView[]) => void;
  onCommandCreated: (command: OpsCommand) => void;
}) {
  const [message, setMessage] = useState<string | null>(null);
  const [expandedRepos, setExpandedRepos] = useState<Set<string>>(new Set());

  function toggleExpand(id: string) {
    setExpandedRepos((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function refreshRepositories() {
    const response = await fetch("/api/repositories", { cache: "no-store" });
    if (!response.ok) {
      setMessage("Repository refresh failed.");
      return;
    }

    onRepositoriesChange((await response.json()) as RepositoryDeploymentView[]);
  }

  async function toggleAutoDeploy(repository: RepositoryDeploymentView) {
    const response = await fetch(`/api/repositories/${repository.repository.id}/settings`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ autoDeployEnabled: !repository.repository.autoDeployEnabled }),
    });

    if (!response.ok) {
      setMessage("Auto deploy update failed.");
      return;
    }

    const updated = await response.json();
    onRepositoriesChange(
      repositories.map((item) =>
        item.repository.id === repository.repository.id
          ? { ...item, repository: updated }
          : item,
      ),
    );
    setMessage(`${repository.repository.fullName} auto deploy updated.`);
  }

  async function syncRepository(repository: RepositoryDeploymentView) {
    const response = await fetch(`/api/repositories/${repository.repository.id}/sync`, {
      method: "POST",
    });

    if (!response.ok) {
      setMessage("GitHub sync failed.");
      return;
    }

    await refreshRepositories();
    setMessage(`${repository.repository.fullName} synced.`);
  }

  async function deployLatest(repository: RepositoryDeploymentView) {
    const response = await fetch("/api/commands", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        projectId,
        action: toWireCommandAction(repository.repository.deployAction),
        target: null,
        requestedBy: "dashboard-user",
        confirmation: projectId,
      } satisfies CreateCommandInput),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: "Deploy request failed." }));
      setMessage(error.error ?? "Deploy request failed.");
      return;
    }

    const command = (await response.json()) as OpsCommand;
    onCommandCreated(command);
    setMessage(`${repository.repository.fullName} deploy queued.`);
  }

  return (
    <section className="deploymentGrid">
      {repositories.length === 0 ? (
        <section className="panel">
          <span className="sectionLabel">Deployments</span>
          <h2>No repositories connected</h2>
        </section>
      ) : (
        repositories.map((repository) => (
          <article
            className="repoCard"
            key={repository.repository.id}
            data-auto={repository.repository.autoDeployEnabled ? "on" : "off"}
            data-deploy-status={repository.latestCommand?.status ?? "none"}
          >
            {/* ── Card Header ── */}
            <div className="repoCardHeader">
              <div className="repoCardTitle">
                <h3>{repository.repository.fullName}</h3>
              </div>
              <button
                className={`autoDeployToggle ${repository.repository.autoDeployEnabled ? "isOn" : ""}`}
                onClick={() => toggleAutoDeploy(repository)}
                type="button"
                title={
                  repository.repository.autoDeployEnabled
                    ? "Auto-deploy ON — click to disable"
                    : "Auto-deploy OFF — click to enable"
                }
              >
                <span className="toggleTrack">
                  <span className="toggleThumb" />
                </span>
                <span className="toggleLabel">
                  {repository.repository.autoDeployEnabled ? "Auto" : "Manual"}
                </span>
              </button>
            </div>

            {/* ── Branch & Last Commit ── */}
            <div className="repoBranchRow">
              <span className="repoBranch">
                <GitBranch aria-hidden="true" />
                {repository.repository.branch}
              </span>
              {repository.latestCommit && (
                <span className="repoSha">
                  <span className="shaChip">{repository.latestCommit.sha.slice(0, 7)}</span>
                  <Clock3 aria-hidden="true" />
                  {formatRelativeTime(repository.latestCommit.committedAt)}
                </span>
              )}
            </div>

            {/* ── Last Deploy Status Banner ── */}
            <div
              className={`deployStatusBanner ${
                repository.latestCommand
                  ? `status-${repository.latestCommand.status}`
                  : "status-none"
              }`}
            >
              <span className="deployStatusDot" />
              <span className="deployStatusText">
                {repository.latestCommand
                  ? `Last deploy · ${repository.latestCommand.status}`
                  : "No deployments yet"}
              </span>
              {repository.latestCommand && (
                <span className="deployStatusAction">
                  {toWireCommandAction(repository.latestCommand.action)}
                </span>
              )}
            </div>

            {/* ── Action Row ── */}
            <div className="repoActionRow">
              <button
                className="repoActionPrimary"
                onClick={() => deployLatest(repository)}
                type="button"
              >
                <Activity aria-hidden="true" />
                Deploy
              </button>
              <button
                className="repoActionSecondary"
                onClick={() => syncRepository(repository)}
                type="button"
              >
                <RefreshCcw aria-hidden="true" />
                Sync
              </button>
            </div>

            {/* ── Commit Timeline ── */}
            <div className="repoCommitList">
              <span className="repoCommitListLabel">Recent commits</span>
              {repository.recentCommits.length === 0 ? (
                <div className="repoEmptyCommits">No commits synced yet</div>
              ) : (() => {
                const isExpanded = expandedRepos.has(repository.repository.id);
                const visibleCommits = isExpanded
                  ? repository.recentCommits
                  : repository.recentCommits.slice(0, 5);
                const hasMore = repository.recentCommits.length > 5;
                return (
                  <>
                    {visibleCommits.map((commit) => {
                      const { type, rest } = parseCommitMessage(
                        commit.message.split("\n")[0],
                      );
                      return (
                        <a
                          className="repoCommitEntry"
                          href={commit.url}
                          key={`${repository.repository.id}-${commit.sha}`}
                          rel="noreferrer"
                          target="_blank"
                        >
                          {type && (
                            <span className={`commitTypeChip type-${type}`}>{type}</span>
                          )}
                          <span className="repoCommitBody">
                            <span className="repoCommitMsg">
                              {type ? rest : commit.message.split("\n")[0]}
                            </span>
                            <span className="repoCommitMeta">
                              <span>{commit.authorLogin || commit.authorName}</span>
                              <span className="shaChip">{commit.sha.slice(0, 7)}</span>
                            </span>
                          </span>
                          <ExternalLink aria-hidden="true" className="repoCommitLink" />
                        </a>
                      );
                    })}
                    {hasMore && (
                      <button
                        className="repoExpandBtn"
                        onClick={() => toggleExpand(repository.repository.id)}
                        type="button"
                      >
                        {isExpanded
                          ? "Show less"
                          : `Show ${repository.recentCommits.length - 5} more`}
                      </button>
                    )}
                  </>
                );
              })()}
            </div>
          </article>
        ))
      )}
      {message && (
        <p className="deployMessage">
          <CheckCircle2 aria-hidden="true" size={14} /> {message}
        </p>
      )}
    </section>
  );
}

function toWireCommandAction(action: string) {
  return action.replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`).replace(/^_/, "");
}

function formatShortTime(value: string) {
  return new Intl.DateTimeFormat("th-TH", {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "Asia/Bangkok"
  }).format(new Date(value));
}

function formatThaiTime(isoString: string) {
  return new Intl.DateTimeFormat("th-TH", {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    timeZone: "Asia/Bangkok"
  }).format(new Date(isoString));
}

function formatRelativeTime(value: string) {
  const diff = Date.now() - new Date(value).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  return formatShortTime(value);
}

const COMMIT_TYPE_RE = /^(feat|fix|refactor|chore|docs|test|style|perf|ci|build|revert)(\([^)]+\))?:\s*/;

function parseCommitMessage(message: string): { type: string | null; rest: string } {
  const match = message.match(COMMIT_TYPE_RE);
  if (!match) return { type: null, rest: message };
  return { type: match[1], rest: message.slice(match[0].length) };
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
