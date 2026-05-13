import assert from "node:assert/strict";
import { test } from "node:test";
import { formatOsName } from "../src/system.js";

test("formats Linux distro name from os-release PRETTY_NAME", () => {
  const osRelease = [
    'PRETTY_NAME="Ubuntu 22.04.4 LTS"',
    'NAME="Ubuntu"',
    "VERSION_ID=22.04",
  ].join("\n");

  assert.equal(formatOsName("linux", "6.8.0-100-generic", osRelease), "Ubuntu 22.04.4 LTS");
});

test("falls back to platform and kernel release when os-release is unavailable", () => {
  assert.equal(formatOsName("linux", "6.8.0-100-generic", null), "linux 6.8.0-100-generic");
});
