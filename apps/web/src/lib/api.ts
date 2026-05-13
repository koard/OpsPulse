import { sampleDashboardPayload, sampleSrePayload } from "./sample-data";
import type { AgentStatus, DashboardPayload, Incident, OpsCommand, SloReport, SrePayload } from "./types";

export async function getDashboardPayload(): Promise<DashboardPayload> {
  const apiBaseUrl = process.env.API_BASE_URL;

  if (!apiBaseUrl) {
    return sampleDashboardPayload;
  }

  try {
    const response = await fetch(`${apiBaseUrl}/api/dashboard`, {
      cache: "no-store",
    });

    if (!response.ok) {
      return sampleDashboardPayload;
    }

    return (await response.json()) as DashboardPayload;
  } catch {
    return sampleDashboardPayload;
  }
}

export async function getSrePayload(): Promise<SrePayload> {
  const dashboard = await getDashboardPayload();
  const projectId = dashboard.projects[0]?.id;
  const apiBaseUrl = process.env.API_BASE_URL;

  if (!apiBaseUrl || !projectId) {
    return { ...sampleSrePayload, dashboard };
  }

  const [incidents, agents, commands, slo] = await Promise.all([
    fetchJsonOrDefault<Incident[]>(`${apiBaseUrl}/api/incidents`, sampleSrePayload.incidents),
    fetchJsonOrDefault<AgentStatus[]>(`${apiBaseUrl}/api/agents`, sampleSrePayload.agents),
    fetchJsonOrDefault<OpsCommand[]>(`${apiBaseUrl}/api/commands?projectId=${projectId}`, sampleSrePayload.commands),
    fetchJsonOrDefault<SloReport | null>(`${apiBaseUrl}/api/slo/${projectId}`, sampleSrePayload.slo ?? null),
  ]);

  return { dashboard, incidents, agents, commands, slo };
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
