import assert from "node:assert/strict";
import { test } from "node:test";
import { planCommandSteps } from "../src/commands.js";

const baseConfig = {
  projectId: "dukefarm-production",
  dukeFarmBaseUrl: "http://127.0.0.1:4000",
  dukeFarmBackendDir: "/srv/DukeFarm-Backend",
  dukeFarmBranch: "main",
};

test("plans redeploy backend in production-safe order", () => {
  const steps = planCommandSteps(
    { action: "redeployBackend", target: "dukefarm-backend" },
    baseConfig,
  );

  assert.deepEqual(
    steps.map((step) => [step.command, step.args, step.cwd]),
    [
      ["git", ["fetch", "origin", "main"], "/srv/DukeFarm-Backend"],
      ["git", ["reset", "--hard", "origin/main"], "/srv/DukeFarm-Backend"],
      ["git", ["rev-parse", "HEAD"], "/srv/DukeFarm-Backend"],
      ["npm", ["ci"], "/srv/DukeFarm-Backend"],
      ["npm", ["run", "prisma:generate"], "/srv/DukeFarm-Backend"],
      ["npm", ["run", "build"], "/srv/DukeFarm-Backend"],
      ["pm2", ["restart", "dukefarm-backend"], undefined],
    ],
  );
});

test("rejects restart target outside allowlist", () => {
  assert.throws(
    () => planCommandSteps(
      { action: "pm2RestartProcess", target: "database" },
      baseConfig,
    ),
    /not allowlisted/,
  );
});

test("rejects unsupported arbitrary command action", () => {
  assert.throws(
    () => planCommandSteps(
      { action: "shell", target: "rm -rf /" },
      baseConfig,
    ),
    /Unsupported command action/,
  );
});
