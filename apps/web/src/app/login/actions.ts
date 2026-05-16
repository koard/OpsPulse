"use server";

import { cookies, headers } from "next/headers";
import { redirect } from "next/navigation";
import {
  AUTH_COOKIE_NAME,
  SESSION_TTL_MS,
  createSessionToken,
  loginRateLimiter,
  verifyPassword,
} from "@/lib/auth";
import { sanitizeNextPath } from "@/lib/auth-routing";

export async function loginAction(formData: FormData) {
  const nextPath = sanitizeNextPath(formData.get("next"));
  const password = formData.get("password");
  const clientIp = await getClientIp();
  const rateLimit = loginRateLimiter.canAttempt(clientIp);

  if (!rateLimit.allowed) {
    redirectToLoginError(nextPath);
  }

  if (typeof password !== "string" || !verifyPassword(password)) {
    loginRateLimiter.recordFailure(clientIp);
    redirectToLoginError(nextPath);
  }

  loginRateLimiter.reset(clientIp);

  try {
    const expires = new Date(Date.now() + SESSION_TTL_MS);
    const cookieStore = await cookies();

    cookieStore.set(AUTH_COOKIE_NAME, createSessionToken(), {
      expires,
      httpOnly: true,
      sameSite: "lax",
      secure: process.env.NODE_ENV === "production",
      path: "/",
    });
  } catch {
    redirectToLoginError(nextPath);
  }

  redirect(nextPath);
}

async function getClientIp() {
  const headerStore = await headers();
  const forwardedFor = headerStore.get("x-forwarded-for");
  const realIp = headerStore.get("x-real-ip");

  return forwardedFor?.split(",")[0]?.trim() || realIp || "unknown";
}

function redirectToLoginError(nextPath: string): never {
  const searchParams = new URLSearchParams({
    error: "1",
    next: nextPath,
  });

  redirect(`/login?${searchParams.toString()}`);
}
