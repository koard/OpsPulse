import { createHmac, createHash, timingSafeEqual } from "node:crypto";

export const AUTH_COOKIE_NAME = "opspulse_session";
export const SESSION_TTL_MS = 12 * 60 * 60 * 1000;

type SessionTokenOptions = {
  now?: number;
  ttlMs?: number;
};

type VerifySessionOptions = {
  now?: number;
};

type SessionPayload = {
  exp: number;
  iat: number;
  v: 1;
};

type LoginAttempt = {
  attempts: number;
  firstFailedAt: number;
  lockedUntil: number;
};

export type LoginRateLimitStatus = {
  allowed: boolean;
  retryAfterMs: number;
};

type LoginRateLimiterOptions = {
  lockMs: number;
  maxAttempts: number;
  now?: () => number;
  windowMs: number;
};

export function verifyPassword(candidate: string): boolean {
  const expected = process.env.OPSPULSE_AUTH_PASSWORD;

  if (!candidate || !expected) {
    return false;
  }

  return constantTimeStringEqual(candidate, expected);
}

export function createSessionToken(options: SessionTokenOptions = {}): string {
  const now = options.now ?? Date.now();
  const ttlMs = options.ttlMs ?? SESSION_TTL_MS;
  const payload: SessionPayload = {
    exp: now + ttlMs,
    iat: now,
    v: 1,
  };
  const encodedPayload = encodeBase64Url(JSON.stringify(payload));
  const signature = sign(encodedPayload);

  return `${encodedPayload}.${signature}`;
}

export function verifySessionToken(
  token: string | undefined,
  options: VerifySessionOptions = {},
): boolean {
  if (!token) {
    return false;
  }

  const [encodedPayload, signature, extra] = token.split(".");
  if (!encodedPayload || !signature || extra !== undefined) {
    return false;
  }

  try {
    const expectedSignature = sign(encodedPayload);
    if (!constantTimeStringEqual(signature, expectedSignature)) {
      return false;
    }

    const payload = JSON.parse(decodeBase64Url(encodedPayload)) as Partial<SessionPayload>;
    const now = options.now ?? Date.now();

    return payload.v === 1 && typeof payload.exp === "number" && payload.exp > now;
  } catch {
    return false;
  }
}

export function createLoginRateLimiter(options: LoginRateLimiterOptions) {
  const attempts = new Map<string, LoginAttempt>();
  const now = options.now ?? Date.now;

  function normalize(key: string) {
    return key.trim() || "unknown";
  }

  return {
    canAttempt(key: string): LoginRateLimitStatus {
      const normalizedKey = normalize(key);
      const attempt = attempts.get(normalizedKey);
      const currentTime = now();

      if (!attempt) {
        return { allowed: true, retryAfterMs: 0 };
      }

      if (attempt.lockedUntil > currentTime) {
        return {
          allowed: false,
          retryAfterMs: attempt.lockedUntil - currentTime,
        };
      }

      if (
        attempt.lockedUntil > 0 ||
        currentTime - attempt.firstFailedAt >= options.windowMs
      ) {
        attempts.delete(normalizedKey);
      }

      return { allowed: true, retryAfterMs: 0 };
    },
    recordFailure(key: string) {
      const normalizedKey = normalize(key);
      const currentTime = now();
      const existing = attempts.get(normalizedKey);
      const attempt =
        existing && currentTime - existing.firstFailedAt < options.windowMs
          ? existing
          : { attempts: 0, firstFailedAt: currentTime, lockedUntil: 0 };

      attempt.attempts += 1;
      if (attempt.attempts >= options.maxAttempts) {
        attempt.lockedUntil = currentTime + options.lockMs;
      }

      attempts.set(normalizedKey, attempt);
    },
    reset(key: string) {
      attempts.delete(normalize(key));
    },
  };
}

export const loginRateLimiter = createLoginRateLimiter({
  lockMs: 15 * 60 * 1000,
  maxAttempts: 5,
  windowMs: 10 * 60 * 1000,
});

function sign(value: string): string {
  const secret = process.env.OPSPULSE_AUTH_SESSION_SECRET;
  if (!secret) {
    throw new Error("OPSPULSE_AUTH_SESSION_SECRET is not configured.");
  }

  return createHmac("sha256", secret).update(value).digest("base64url");
}

function constantTimeStringEqual(left: string, right: string): boolean {
  const leftHash = createHash("sha256").update(left).digest();
  const rightHash = createHash("sha256").update(right).digest();

  return timingSafeEqual(leftHash, rightHash);
}

function encodeBase64Url(value: string): string {
  return Buffer.from(value, "utf8").toString("base64url");
}

function decodeBase64Url(value: string): string {
  return Buffer.from(value, "base64url").toString("utf8");
}
