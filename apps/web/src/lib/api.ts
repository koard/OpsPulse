import type {
  AgentStatus,
  DashboardPayload,
  Incident,
  OpsCommand,
  RepositoryDeploymentView,
  SloReport,
  SrePayload,
} from "./types";

const emptyDashboardPayload = (): DashboardPayload => ({
  generatedAt: new Date().toISOString(),
  projects: [],
});

export async function getDashboardPayload(): Promise<DashboardPayload> {
  const apiBaseUrl = process.env.API_BASE_URL;

  if (!apiBaseUrl) {
    return emptyDashboardPayload();
  }

  try {
    const response = await fetch(`${apiBaseUrl}/api/dashboard`, {
      cache: "no-store",
    });

    if (!response.ok) {
      return emptyDashboardPayload();
    }

    return (await response.json()) as DashboardPayload;
  } catch {
    return emptyDashboardPayload();
  }
}

export async function getSrePayload(): Promise<SrePayload> {
  const dashboard = await getDashboardPayload();
  const projectId = dashboard.projects[0]?.id;
  const apiBaseUrl = process.env.API_BASE_URL;

  if (!apiBaseUrl || !projectId) {
    return { dashboard, incidents: [], agents: [], commands: [], repositories: [], slo: null };
  }

  const [incidents, agents, commands, repositories, slo] = await Promise.all([
    fetchJsonOrDefault<Incident[]>(`${apiBaseUrl}/api/incidents`, []),
    fetchJsonOrDefault<AgentStatus[]>(`${apiBaseUrl}/api/agents`, []),
    fetchJsonOrDefault<OpsCommand[]>(`${apiBaseUrl}/api/commands?projectId=${projectId}`, []),
    fetchJsonOrDefault<RepositoryDeploymentView[]>(`${apiBaseUrl}/api/repositories`, []),
    fetchJsonOrDefault<SloReport | null>(`${apiBaseUrl}/api/slo/${projectId}`, null),
  ]);

  return { dashboard, incidents, agents, commands, repositories, slo };
}

async function fetchJsonOrDefault<T>(url: string, fallback: T): Promise<T> {
  try {
    const response = await fetch(url, { cache: "no-store" });
    if (!response.ok) {
      return fallback;
    }

    return (await response.json()) as T;
  } catch {
    return fallback;
  }
}
