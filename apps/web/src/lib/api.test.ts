import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("API payload fallbacks", () => {
  const originalApiBaseUrl = process.env.API_BASE_URL;

  beforeEach(() => {
    vi.resetModules();
    vi.unstubAllGlobals();
    delete process.env.API_BASE_URL;
  });

  afterEach(() => {
    if (originalApiBaseUrl === undefined) {
      delete process.env.API_BASE_URL;
    } else {
      process.env.API_BASE_URL = originalApiBaseUrl;
    }
  });

  it("returns an empty dashboard instead of sample data when API_BASE_URL is missing", async () => {
    const { getDashboardPayload } = await import("./api");

    await expect(getDashboardPayload()).resolves.toMatchObject({
      projects: [],
    });
  });

  it("returns empty SRE collections when no real project is available", async () => {
    const { getSrePayload } = await import("./api");

    await expect(getSrePayload()).resolves.toMatchObject({
      dashboard: { projects: [] },
      incidents: [],
      agents: [],
      commands: [],
      repositories: [],
      slo: null,
    });
  });
});
