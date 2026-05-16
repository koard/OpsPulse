import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("password auth", () => {
  const originalPassword = process.env.OPSPULSE_AUTH_PASSWORD;
  const originalSecret = process.env.OPSPULSE_AUTH_SESSION_SECRET;

  beforeEach(() => {
    vi.resetModules();
    process.env.OPSPULSE_AUTH_PASSWORD = "correct horse battery staple";
    process.env.OPSPULSE_AUTH_SESSION_SECRET = "0123456789abcdef0123456789abcdef";
  });

  afterEach(() => {
    if (originalPassword === undefined) {
      delete process.env.OPSPULSE_AUTH_PASSWORD;
    } else {
      process.env.OPSPULSE_AUTH_PASSWORD = originalPassword;
    }

    if (originalSecret === undefined) {
      delete process.env.OPSPULSE_AUTH_SESSION_SECRET;
    } else {
      process.env.OPSPULSE_AUTH_SESSION_SECRET = originalSecret;
    }
  });

  it("accepts the configured dashboard password", async () => {
    const { verifyPassword } = await import("./auth");

    expect(verifyPassword("correct horse battery staple")).toBe(true);
  });

  it("rejects wrong passwords", async () => {
    const { verifyPassword } = await import("./auth");

    expect(verifyPassword("wrong password")).toBe(false);
  });

  it("fails closed when no password is configured", async () => {
    delete process.env.OPSPULSE_AUTH_PASSWORD;
    const { verifyPassword } = await import("./auth");

    expect(verifyPassword("correct horse battery staple")).toBe(false);
  });

  it("verifies an untampered session token", async () => {
    const { createSessionToken, verifySessionToken } = await import("./auth");
    const token = createSessionToken({ now: 1_000, ttlMs: 60_000 });

    expect(verifySessionToken(token, { now: 30_000 })).toBe(true);
  });

  it("rejects a tampered session token", async () => {
    const { createSessionToken, verifySessionToken } = await import("./auth");
    const token = createSessionToken({ now: 1_000, ttlMs: 60_000 });
    const replacement = token.endsWith("a") ? "b" : "a";
    const tampered = `${token.slice(0, -1)}${replacement}`;

    expect(verifySessionToken(tampered, { now: 30_000 })).toBe(false);
  });

  it("rejects an expired session token", async () => {
    const { createSessionToken, verifySessionToken } = await import("./auth");
    const token = createSessionToken({ now: 1_000, ttlMs: 60_000 });

    expect(verifySessionToken(token, { now: 62_000 })).toBe(false);
  });

  it("fails closed when session secret is missing", async () => {
    const { createSessionToken, verifySessionToken } = await import("./auth");
    const token = createSessionToken({ now: 1_000, ttlMs: 60_000 });
    delete process.env.OPSPULSE_AUTH_SESSION_SECRET;

    expect(verifySessionToken(token, { now: 30_000 })).toBe(false);
  });
});

describe("login rate limiter", () => {
  it("blocks after five failed attempts and resets after success", async () => {
    const { createLoginRateLimiter } = await import("./auth");
    const now = 0;
    const limiter = createLoginRateLimiter({
      maxAttempts: 5,
      windowMs: 10 * 60 * 1000,
      lockMs: 15 * 60 * 1000,
      now: () => now,
    });

    for (let attempt = 0; attempt < 5; attempt += 1) {
      expect(limiter.canAttempt("127.0.0.1").allowed).toBe(true);
      limiter.recordFailure("127.0.0.1");
    }

    expect(limiter.canAttempt("127.0.0.1")).toMatchObject({
      allowed: false,
      retryAfterMs: 15 * 60 * 1000,
    });

    limiter.reset("127.0.0.1");

    expect(limiter.canAttempt("127.0.0.1").allowed).toBe(true);
  });

  it("allows attempts again after the lock expires", async () => {
    const { createLoginRateLimiter } = await import("./auth");
    let now = 0;
    const limiter = createLoginRateLimiter({
      maxAttempts: 5,
      windowMs: 10 * 60 * 1000,
      lockMs: 15 * 60 * 1000,
      now: () => now,
    });

    for (let attempt = 0; attempt < 5; attempt += 1) {
      limiter.recordFailure("127.0.0.1");
    }

    now = 15 * 60 * 1000 + 1;

    expect(limiter.canAttempt("127.0.0.1").allowed).toBe(true);
  });
});
