export type ProcessStatus = "online" | "stopped" | "errored" | "restarting";
export type Severity = "healthy" | "warning" | "critical";
export type AlertSeverity = "info" | "warning" | "critical";
export type IncidentStatus = "open" | "acknowledged" | "resolved";
export type OpsCommandAction =
  | "healthCheckNow"
  | "pm2RestartProcess"
  | "redeployBackend"
  | "redeployFrontend"
  | "redeployAdmin"
  | "prismaMigrateDeploy"
  | "rollbackBackend"
  | "rollbackFrontend"
  | "rollbackAdmin";
export type OpsCommandStatus =
  | "pending"
  | "claimed"
  | "running"
  | "succeeded"
  | "failed"
  | "timedOut"
  | "cancelled";

export type ServerProfile = {
  hostname: string;
  os: string;
  access: string;
  reverseProxy: string;
  processManager: string;
};

export type EndpointCheck = {
  name: string;
  url: string;
  statusCode: number;
  latencyMs: number;
  checkedAt: string;
};

export type ProcessMetric = {
  name: string;
  role: string;
  status: ProcessStatus;
  cpuPercent: number;
  memoryMb: number;
  restarts: number;
  uptimeSeconds: number;
};

export type HostMetrics = {
  cpuPercent: number;
  memoryPercent: number;
  diskPercent: number;
  networkInMbps: number;
  networkOutMbps: number;
};

export type AlertEvent = {
  id: string;
  severity: AlertSeverity;
  title: string;
  message: string;
  createdAt: string;
  acknowledged: boolean;
};

export type Incident = {
  id: string;
  projectId: string;
  fingerprint: string;
  severity: AlertSeverity;
  title: string;
  summary: string;
  status: IncidentStatus;
  startedAt: string;
  lastSeenAt: string;
  rootCauseHint: string;
  occurrences: number;
  acknowledgedAt?: string | null;
  resolvedAt?: string | null;
};

export type DriftIssue = {
  kind: "missingExpectedProcess" | "unknownProcess" | "staleAgent";
  message: string;
};

export type AgentMetadata = {
  version: string;
  hostname: string;
  os: string;
  pm2Version: string;
  receivedAt: string;
};

export type AgentStatus = {
  projectId: string;
  agent?: AgentMetadata | null;
  isStale: boolean;
  lastSeenAt?: string | null;
  driftIssues: DriftIssue[];
};

export type EndpointSlo = {
  name: string;
  url: string;
  availabilityPercent: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  totalChecks: number;
  failedChecks: number;
};

export type SloReport = {
  projectId: string;
  targetAvailabilityPercent: number;
  availabilityPercent: number;
  errorBudgetBurnedPercent: number;
  endpoints: EndpointSlo[];
};

export type OpsCommand = {
  id: string;
  projectId: string;
  action: OpsCommandAction;
  target: string;
  status: OpsCommandStatus;
  requestedBy: string;
  requestedAt: string;
  claimedAt?: string | null;
  finishedAt?: string | null;
  summary?: string | null;
  stdoutTail?: string | null;
  stderrTail?: string | null;
  releaseCommit?: string | null;
};

export type CreateCommandInput = {
  projectId: string;
  action: string;
  target?: string | null;
  requestedBy: string;
  confirmation: string;
};

export type TimelinePoint = {
  at: string;
  healthScore: number;
  latencyMs: number;
};

export type MonitoredProject = {
  id: string;
  name: string;
  environment: string;
  owner: string;
  server: ServerProfile;
  endpoints: EndpointCheck[];
  processes: ProcessMetric[];
  metrics: HostMetrics;
  alerts: AlertEvent[];
  timeline: TimelinePoint[];
};

export type DashboardPayload = {
  generatedAt: string;
  projects: MonitoredProject[];
};

export type SrePayload = {
  dashboard: DashboardPayload;
  incidents: Incident[];
  agents: AgentStatus[];
  commands: OpsCommand[];
  slo?: SloReport | null;
};

export type DashboardSummary = {
  totalProjects: number;
  onlineProcesses: number;
  degradedProjects: number;
  openCriticalAlerts: number;
};
