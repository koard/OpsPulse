import assert from "node:assert/strict";
import { test } from "node:test";
import { executeOpsCommand, planCommandSteps } from "../src/commands.js";

const baseConfig = {
  projectId: "dukefarm",
  dukeFarmBaseUrl: "http://127.0.0.1:4000",
  dukeFarmBackendDir: "/srv/DukeFarm-Backend",
  dukeFarmFrontendDir: "/srv/DukeFarm-Frontend",
  dukeFarmAdminDir: "/srv/DukeFarm-Admin",
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
      ["git", ["status", "--porcelain"], "/srv/DukeFarm-Backend"],
      ["git", ["reset", "--hard", "origin/main"], "/srv/DukeFarm-Backend"],
      ["git", ["rev-parse", "HEAD"], "/srv/DukeFarm-Backend"],
      ["npm", ["ci"], "/srv/DukeFarm-Backend"],
      ["npm", ["run", "prisma:generate"], "/srv/DukeFarm-Backend"],
      ["npm", ["run", "build"], "/srv/DukeFarm-Backend"],
      ["pm2", ["restart", "dukefarm-backend"], undefined],
    ],
  );
});

test("plans redeploy frontend with frontend directory and PM2 target", () => {
  const steps = planCommandSteps(
    { action: "redeployFrontend", target: "dukefarm-frontend" },
    baseConfig,
  );

  assert.deepEqual(
    steps.map((step) => [step.command, step.args, step.cwd]),
    [
      ["git", ["fetch", "origin", "main"], "/srv/DukeFarm-Frontend"],
      ["git", ["status", "--porcelain"], "/srv/DukeFarm-Frontend"],
      ["git", ["reset", "--hard", "origin/main"], "/srv/DukeFarm-Frontend"],
      ["git", ["rev-parse", "HEAD"], "/srv/DukeFarm-Frontend"],
      ["npm", ["ci"], "/srv/DukeFarm-Frontend"],
      ["npm", ["run", "build"], "/srv/DukeFarm-Frontend"],
      ["pm2", ["restart", "dukefarm-frontend"], undefined],
    ],
  );
});

test("plans redeploy admin with admin directory and PM2 target", () => {
  const steps = planCommandSteps(
    { action: "redeployAdmin", target: "dukefarm-admin" },
    baseConfig,
  );

  assert.deepEqual(
    steps.map((step) => [step.command, step.args, step.cwd]),
    [
      ["git", ["fetch", "origin", "main"], "/srv/DukeFarm-Admin"],
      ["git", ["status", "--porcelain"], "/srv/DukeFarm-Admin"],
      ["git", ["reset", "--hard", "origin/main"], "/srv/DukeFarm-Admin"],
      ["git", ["rev-parse", "HEAD"], "/srv/DukeFarm-Admin"],
      ["npm", ["ci"], "/srv/DukeFarm-Admin"],
      ["npm", ["run", "build"], "/srv/DukeFarm-Admin"],
      ["pm2", ["restart", "dukefarm-admin"], undefined],
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

test("fails redeploy before reset when worktree has local changes", async () => {
  const result = await executeOpsCommand(
    { action: "redeployFrontend", target: "dukefarm-frontend" },
    baseConfig,
    async (step) => {
      if (step.command === "git" && step.args.join(" ") === "status --porcelain") {
        return { stdout: " M package.json\n", stderr: "" };
      }

      return { stdout: "", stderr: "" };
    },
  );

  assert.equal(result.status, "failed");
  assert.match(result.summary, /worktree has local changes/i);
  assert.match(result.stdout, /package\.json/);
});
