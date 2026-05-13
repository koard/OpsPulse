import type {
  DashboardPayload,
  DashboardSummary,
  AgentStatus,
  Incident,
  MonitoredProject,
  ProcessMetric,
  Severity,
  SloReport,
} from "./types";

export function getProjectSeverity(project: MonitoredProject): Severity {
  const hasDownProcess = project.processes.some(
    (process) => process.status !== "online",
  );
  const hasFailingEndpoint = project.endpoints.some(
    (endpoint) => endpoint.statusCode >= 500 || endpoint.statusCode === 0,
  );
  const hasCriticalAlert = project.alerts.some(
    (alert) => alert.severity === "critical" && !alert.acknowledged,
  );

  if (hasDownProcess || hasFailingEndpoint || hasCriticalAlert) {
    return "critical";
  }

  const hasResourcePressure =
    project.metrics.cpuPercent >= 75 ||
    project.metrics.memoryPercent >= 75 ||
    project.metrics.diskPercent >= 80 ||
    project.endpoints.some((endpoint) => endpoint.latencyMs >= 800);

  return hasResourcePressure ? "warning" : "healthy";
}

export function buildDashboardSummary(
  payload: DashboardPayload,
): DashboardSummary {
  const processes = payload.projects.flatMap((project) => project.processes);

  return {
    totalProjects: payload.projects.length,
    onlineProcesses: processes.filter((process) => process.status === "online")
      .length,
    degradedProjects: payload.projects.filter(
      (project) => getProjectSeverity(project) !== "healthy",
    ).length,
    openCriticalAlerts: payload.projects.flatMap((project) => project.alerts)
      .filter((alert) => alert.severity === "critical" && !alert.acknowledged)
      .length,
  };
}

export function filterProcesses(
  processes: ProcessMetric[],
  searchText: string,
): ProcessMetric[] {
  const normalizedSearch = searchText.trim().toLowerCase();

  if (!normalizedSearch) {
    return processes;
  }

  return processes.filter((process) => {
    const haystack = `${process.name} ${process.role} ${process.status}`
      .toLowerCase();
    return haystack.includes(normalizedSearch);
  });
}

export function formatUptime(totalSeconds: number): string {
  if (totalSeconds < 60) {
    return `${totalSeconds}s`;
  }

  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);

  if (days > 0) {
    return `${days}d ${hours}h`;
  }

  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }

  return `${minutes}m`;
}

export function getHealthScore(project: MonitoredProject): number {
  const latestPoint = project.timeline.at(-1);

  if (latestPoint) {
    return latestPoint.healthScore;
  }

  const severity = getProjectSeverity(project);
  if (severity === "healthy") {
    return 96;
  }

  return severity === "warning" ? 72 : 38;
}

export function getOpenIncidents<T extends Pick<Incident, "status">>(
  incidents: T[],
): T[] {
  return incidents.filter((incident) => incident.status !== "resolved");
}

export function getStaleAgents<T extends Pick<AgentStatus, "isStale">>(
  agents: T[],
): T[] {
  return agents.filter((agent) => agent.isStale);
}

export function summarizeSlo(
  report: Pick<SloReport, "availabilityPercent" | "errorBudgetBurnedPercent">,
): string {
  return `${report.availabilityPercent.toFixed(2)}% availability · ${report.errorBudgetBurnedPercent.toFixed(2)}% budget burned`;
}
