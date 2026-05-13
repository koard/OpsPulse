import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const OUTPUT_LIMIT = 4096;
const DEFAULT_COMMAND_TIMEOUT_MS = 120000;
const allowedProcesses = new Set([
  "dukefarm-backend",
  "dukefarm-admin",
  "dukefarm-frontend",
  "opspulse-agent",
]);

export function planCommandSteps(command, config) {
  const action = normalizeAction(command.action);
  const branch = config.dukeFarmBranch ?? "main";

  switch (action) {
    case "health_check_now":
      return [];
    case "pm2_restart_process":
      assertAllowedProcess(command.target);
      return [{ command: "pm2", args: ["restart", command.target] }];
    case "redeploy_backend":
      return redeploySteps(deployProfile("backend", config), branch, true);
    case "redeploy_frontend":
      return redeploySteps(deployProfile("frontend", config), branch, false);
    case "redeploy_admin":
      return redeploySteps(deployProfile("admin", config), branch, false);
    case "prisma_migrate_deploy":
      return [{ command: "npx", args: ["prisma", "migrate", "deploy"], cwd: deployProfile("backend", config).dir }];
    case "rollback_backend":
      return rollbackSteps(command, deployProfile("backend", config), true);
    case "rollback_frontend":
      return rollbackSteps(command, deployProfile("frontend", config), false);
    case "rollback_admin":
      return rollbackSteps(command, deployProfile("admin", config), false);
    default:
      throw new Error(`Unsupported command action '${command.action}'`);
  }
}

function redeploySteps(profile, branch, includePrismaGenerate) {
  const steps = [
    { command: "git", args: ["fetch", "origin", branch], cwd: profile.dir },
    { command: "git", args: ["reset", "--hard", `origin/${branch}`], cwd: profile.dir },
    { command: "git", args: ["rev-parse", "HEAD"], cwd: profile.dir, captureReleaseCommit: true },
    { command: "npm", args: ["ci"], cwd: profile.dir },
  ];

  if (includePrismaGenerate) {
    steps.push({ command: "npm", args: ["run", "prisma:generate"], cwd: profile.dir });
  }

  steps.push(
    { command: "npm", args: ["run", "build"], cwd: profile.dir },
    { command: "pm2", args: ["restart", profile.process] },
  );

  return steps;
}

function rollbackSteps(command, profile, includePrismaGenerate) {
  if (!command.target) {
    throw new Error("Rollback target commit is required");
  }

  const steps = [
    { command: "git", args: ["reset", "--hard", command.target], cwd: profile.dir },
    { command: "npm", args: ["ci"], cwd: profile.dir },
  ];

  if (includePrismaGenerate) {
    steps.push({ command: "npm", args: ["run", "prisma:generate"], cwd: profile.dir });
  }

  steps.push(
    { command: "npm", args: ["run", "build"], cwd: profile.dir },
    { command: "pm2", args: ["restart", profile.process] },
  );

  return steps;
}

function deployProfile(service, config) {
  const profiles = {
    backend: {
      dir: config.dukeFarmBackendDir,
      envName: "DUKEFARM_BACKEND_DIR",
      process: "dukefarm-backend",
    },
    frontend: {
      dir: config.dukeFarmFrontendDir,
      envName: "DUKEFARM_FRONTEND_DIR",
      process: "dukefarm-frontend",
    },
    admin: {
      dir: config.dukeFarmAdminDir,
      envName: "DUKEFARM_ADMIN_DIR",
      process: "dukefarm-admin",
    },
  };
  const profile = profiles[service];
  if (!profile?.dir) {
    throw new Error(`${profile?.envName ?? service} is required for deployment commands`);
  }

  return profile;
}

export async function executeOpsCommand(command, config, runner = runStep) {
  const started = Date.now();
  let stdout = "";
  let stderr = "";
  let releaseCommit = null;

  try {
    if (normalizeAction(command.action) === "health_check_now") {
      const health = await runHealthChecks(config);
      stdout += JSON.stringify(health, null, 2);
      const failed = health.some((check) => check.statusCode < 200 || check.statusCode >= 400);
      return {
        status: failed ? "failed" : "succeeded",
        summary: failed ? "One or more health checks failed." : "Health checks passed.",
        stdout: tail(stdout),
        stderr: "",
        releaseCommit: null,
      };
    }

    for (const step of planCommandSteps(command, config)) {
      const result = await runner(step, config.commandTimeoutMs ?? DEFAULT_COMMAND_TIMEOUT_MS);
      stdout += `$ ${step.command} ${step.args.join(" ")}\n${result.stdout}\n`;
      stderr += result.stderr ? `${result.stderr}\n` : "";
      if (step.captureReleaseCommit) {
        releaseCommit = result.stdout.trim().split(/\s+/)[0] ?? null;
      }
    }

    if ([
      "redeploy_backend",
      "redeploy_frontend",
      "redeploy_admin",
      "rollback_backend",
      "rollback_frontend",
      "rollback_admin",
    ].includes(normalizeAction(command.action))) {
      const health = await runHealthChecks(config);
      stdout += `health verify\n${JSON.stringify(health, null, 2)}\n`;
      if (health.some((check) => check.statusCode < 200 || check.statusCode >= 400)) {
        return {
          status: "failed",
          summary: "Command executed but health verification failed.",
          stdout: tail(stdout),
          stderr: tail(stderr),
          releaseCommit,
        };
      }
    }

    return {
      status: "succeeded",
      summary: `${command.action} completed.`,
      stdout: tail(stdout),
      stderr: tail(stderr),
      releaseCommit,
    };
  } catch (error) {
    const isTimeout = error?.code === "ETIMEDOUT" || /timed out/i.test(error?.message ?? "");
    return {
      status: isTimeout ? "timed_out" : "failed",
      summary: error instanceof Error ? error.message : "Command failed.",
      stdout: tail(stdout),
      stderr: tail(`${stderr}${error?.stderr ?? ""}`),
      releaseCommit,
      durationMs: Date.now() - started,
    };
  }
}

export async function claimAndExecuteCommand(config) {
  if (!config.commandsUrl) {
    return null;
  }

  const claimResponse = await fetch(`${config.commandsUrl}/claim`, {
    method: "POST",
    headers: agentHeaders(config),
    body: JSON.stringify({ projectId: config.projectId }),
  });

  if (claimResponse.status === 204) {
    return null;
  }

  if (!claimResponse.ok) {
    throw new Error(`claim failed: ${claimResponse.status} ${claimResponse.statusText}`);
  }

  const command = await claimResponse.json();
  const result = await executeOpsCommand(command, config);
  const resultResponse = await fetch(`${config.commandsUrl}/${command.id}/result`, {
    method: "POST",
    headers: agentHeaders(config),
    body: JSON.stringify({
      projectId: config.projectId,
      ...result,
    }),
  });

  if (!resultResponse.ok) {
    throw new Error(`result failed: ${resultResponse.status} ${resultResponse.statusText}`);
  }

  return await resultResponse.json();
}

async function runStep(step, timeoutMs) {
  return await execFileAsync(step.command, step.args, {
    cwd: step.cwd,
    timeout: timeoutMs,
    windowsHide: true,
    maxBuffer: 1024 * 1024,
  });
}

async function runHealthChecks(config) {
  return Promise.all([
    checkEndpoint("DukeFarm API", `${config.dukeFarmBaseUrl}/healthz`),
    checkEndpoint("DukeFarm API v1", `${config.dukeFarmBaseUrl}/api/v1/health`),
  ]);
}

async function checkEndpoint(name, url) {
  const started = Date.now();
  try {
    const response = await fetch(url);
    return {
      name,
      url,
      statusCode: response.status,
      latencyMs: Date.now() - started,
    };
  } catch (error) {
    return {
      name,
      url,
      statusCode: 0,
      latencyMs: Date.now() - started,
      error: error instanceof Error ? error.message : "request failed",
    };
  }
}

function normalizeAction(action) {
  return String(action)
    .replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`)
    .replace(/^_/, "")
    .toLowerCase();
}

function assertAllowedProcess(processName) {
  if (!allowedProcesses.has(processName)) {
    throw new Error(`Process '${processName}' is not allowlisted`);
  }
}

function agentHeaders(config) {
  return {
    "Content-Type": "application/json",
    "X-Agent-Token": config.token,
  };
}

function tail(value) {
  if (!value) {
    return "";
  }

  return value.length <= OUTPUT_LIMIT ? value : value.slice(-OUTPUT_LIMIT);
}
