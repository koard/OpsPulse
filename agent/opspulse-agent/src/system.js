import { readFile } from "node:fs/promises";
import { platform, release } from "node:os";

export async function getOsName() {
  let osRelease = null;

  try {
    osRelease = await readFile("/etc/os-release", "utf8");
  } catch {
    osRelease = null;
  }

  return formatOsName(platform(), release(), osRelease);
}

export function formatOsName(platformName, kernelRelease, osRelease) {
  const prettyName = parseOsReleaseValue(osRelease, "PRETTY_NAME");
  return prettyName || `${platformName} ${kernelRelease}`;
}

function parseOsReleaseValue(content, key) {
  if (!content) {
    return null;
  }

  const line = content
    .split("\n")
    .find((entry) => entry.startsWith(`${key}=`));

  if (!line) {
    return null;
  }

  return line
    .slice(key.length + 1)
    .trim()
    .replace(/^["']|["']$/g, "");
}
