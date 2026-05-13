import { describe, expect, it } from "vitest";
import {
  buildDashboardSummary,
  filterProcesses,
  getOpenIncidents,
  getStaleAgents,
  getProjectSeverity,
  summarizeSlo,
} from "./dashboard";
import type { DashboardPayload } from "./types";

const payload: DashboardPayload = {
  generatedAt: "2026-05-10T10:00:00Z",
  projects: [
    {
      id: "student-portal",
      name: "Student Portal",
      environment: "Production",
      owner: "University IT",
      server: {
        hostname: "uni-prod-01",
        os: "Ubuntu 22.04 LTS",
        access: "VPN only",
        reverseProxy: "Nginx",
        processManager: "PM2",
      },
      endpoints: [
        {
          name: "Admin UI",
          url: "https://admin.example.ac.th",
          statusCode: 200,
          latencyMs: 142,
          checkedAt: "2026-05-10T09:59:30Z",
        },
      ],
      processes: [
        {
          name: "frontend",
          role: "Next.js public app",
          status: "online",
          cpuPercent: 18,
          memoryMb: 180,
          restarts: 1,
          uptimeSeconds: 74000,
        },
        {
          name: "admin",
          role: "Admin dashboard",
          status: "online",
          cpuPercent: 9,
          memoryMb: 132,
          restarts: 0,
          uptimeSeconds: 65000,
        },
        {
          name: "backend",
          role: ".NET API",
          status: "errored",
          cpuPercent: 82,
          memoryMb: 612,
          restarts: 5,
          uptimeSeconds: 320,
        },
      ],
      metrics: {
        cpuPercent: 71,
        memoryPercent: 78,
        diskPercent: 68,
        networkInMbps: 12.4,
        networkOutMbps: 5.8,
      },
      alerts: [
        {
          id: "alert-1",
          severity: "critical",
          title: "backend process is down",
          message: "PM2 reports backend as errored.",
          createdAt: "2026-05-10T09:59:31Z",
          acknowledged: false,
        },
      ],
      timeline: [
        { at: "09:54", healthScore: 91, latencyMs: 120 },
        { at: "09:56", healthScore: 78, latencyMs: 180 },
        { at: "09:58", healthScore: 42, latencyMs: 410 },
      ],
    },
  ],
};

describe("dashboard view model helpers", () => {
  it("marks a project critical when any process is down", () => {
    expect(getProjectSeverity(payload.projects[0])).toBe("critical");
  });

  it("summarizes project, process, and alert counts", () => {
    expect(buildDashboardSummary(payload)).toEqual({
      totalProjects: 1,
      onlineProcesses: 2,
      degradedProjects: 1,
      openCriticalAlerts: 1,
    });
  });

  it("filters processes by search text across name and role", () => {
    expect(filterProcesses(payload.projects[0].processes, "api")).toHaveLength(1);
    expect(filterProcesses(payload.projects[0].processes, "front")[0].name).toBe(
      "frontend",
    );
  });

  it("returns open incidents and stale agents for SRE views", () => {
    expect(
      getOpenIncidents([
        { id: "1", status: "open" },
        { id: "2", status: "resolved" },
      ]),
    ).toHaveLength(1);
    expect(getStaleAgents([{ projectId: "a", isStale: true }, { projectId: "b", isStale: false }])).toHaveLength(1);
  });

  it("summarizes SLO availability and error budget", () => {
    expect(
      summarizeSlo({
        availabilityPercent: 99.4,
        errorBudgetBurnedPercent: 61.2,
        endpoints: [],
      }),
    ).toEqual("99.40% availability · 61.20% budget burned");
  });
});
