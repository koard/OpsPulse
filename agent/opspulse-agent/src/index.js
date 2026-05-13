import { execFile } from "node:child_process";
import { hostname } from "node:os";
import { promisify } from "node:util";
import { claimAndExecuteCommand } from "./commands.js";
import { getOsName } from "./system.js";

const execFileAsync = promisify(execFile);

const config = {
  projectId: process.env.PROJECT_ID ?? "dukefarm",
  ingestUrl: process.env.OPSPULSE_INGEST_URL,
  token: process.env.OPSPULSE_AGENT_TOKEN,
  intervalMs: Number(process.env.OPSPULSE_INTERVAL_MS ?? 60000),
  commandsUrl: process.env.OPSPULSE_COMMANDS_URL,
  commandPollMs: Number(process.env.OPSPULSE_COMMAND_POLL_MS ?? 15000),
  commandTimeoutMs: Number(process.env.OPSPULSE_COMMAND_TIMEOUT_MS ?? 120000),
  dukeFarmBaseUrl: process.env.DUKEFARM_BASE_URL ?? "http://127.0.0.1:4000",
  dukeFarmBackendDir: process.env.DUKEFARM_BACKEND_DIR,
  dukeFarmFrontendDir: process.env.DUKEFARM_FRONTEND_DIR,
  dukeFarmAdminDir: process.env.DUKEFARM_ADMIN_DIR,
  dukeFarmBranch: process.env.DUKEFARM_BRANCH ?? "main",
};

if (!config.ingestUrl || !config.token) {
  throw new Error("OPSPULSE_INGEST_URL and OPSPULSE_AGENT_TOKEN are required");
}

async function main() {
  await collectAndSend();
  setInterval(() => {
    collectAndSend().catch((error) => {
      console.error("[opspulse-agent] collect failed", error);
    });
  }, config.intervalMs);

  if (config.commandsUrl) {
    await pollCommands();
    setInterval(() => {
      pollCommands().catch((error) => {
        console.error("[opspulse-agent] command poll failed", error);
      });
    }, config.commandPollMs);
  }
}

let commandInFlight = false;

async function pollCommands() {
  if (commandInFlight) {
    return;
  }

  commandInFlight = true;
  try {
    const result = await claimAndExecuteCommand(config);
    if (result) {
      console.log(`[opspulse-agent] command ${result.id} ${result.status}`);
    }
  } finally {
    commandInFlight = false;
  }
}

async function collectAndSend() {
  const [processes, metrics, endpoints, pm2Version, osName] = await Promise.all([
    collectPm2Processes(),
    collectHostMetrics(),
    collectEndpoints(),
    getPm2Version(),
    getOsName(),
  ]);

  const now = new Date().toISOString();
  const payload = {
    projectId: config.projectId,
    capturedAt: now,
    metrics,
    processes,
    endpoints,
    agent: {
      version: "1.0.0",
      hostname: hostname(),
      os: osName,
      pm2Version,
      receivedAt: now,
    },
  };

  const response = await fetch(config.ingestUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Agent-Token": config.token,
      "X-Agent-Version": "1.0.0",
      "X-Agent-Hostname": hostname(),
      "X-Agent-Os": osName,
      "X-Agent-Pm2-Version": pm2Version,
    },
    body: JSON.stringify(payload),
  });

  if (!response.ok) {
    throw new Error(`ingest failed: ${response.status} ${response.statusText}`);
  }

  console.log(`[opspulse-agent] sent snapshot ${now}`);
}

async function collectPm2Processes() {
  const { stdout } = await execFileAsync("pm2", ["jlist"]);
  const processes = JSON.parse(stdout);
  return processes.map((process) => ({
    name: process.name,
    role: process.pm2_env?.name ?? process.name,
    status: process.pm2_env?.status ?? "unknown",
    cpuPercent: Number(process.monit?.cpu ?? 0),
    memoryMb: Math.floor(Number(process.monit?.memory ?? 0) / 1024 / 1024),
    restarts: Number(process.pm2_env?.restart_time ?? 0),
    uptimeSeconds: Math.max(
      0,
      Math.floor((Date.now() - Number(process.pm2_env?.pm_uptime ?? Date.now())) / 1000),
    ),
  }));
}

async function collectHostMetrics() {
  const [cpuPercent, memoryPercent, diskPercent] = await Promise.all([
    shellNumber("bash", ["-lc", "top -bn1 | awk '/Cpu/ { print 100 - $8 }'"]),
    shellNumber("bash", ["-lc", "free | awk '/Mem:/ { printf \"%.1f\", $3 / $2 * 100 }'"]),
    shellNumber("bash", ["-lc", "df / | awk 'NR==2 { gsub(\"%\", \"\", $5); print $5 }'"]),
  ]);

  return {
    cpuPercent,
    memoryPercent,
    diskPercent,
    networkInMbps: 0,
    networkOutMbps: 0,
  };
}

async function collectEndpoints() {
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
      checkedAt: new Date().toISOString(),
    };
  } catch {
    return {
      name,
      url,
      statusCode: 0,
      latencyMs: Date.now() - started,
      checkedAt: new Date().toISOString(),
    };
  }
}

async function getPm2Version() {
  try {
    const { stdout } = await execFileAsync("pm2", ["--version"]);
    return stdout.trim();
  } catch {
    return "unknown";
  }
}

async function shellNumber(command, args) {
  const { stdout } = await execFileAsync(command, args);
  const value = Number(stdout.trim());
  return Number.isFinite(value) ? value : 0;
}

main().catch((error) => {
  console.error("[opspulse-agent] fatal", error);
  process.exit(1);
});
