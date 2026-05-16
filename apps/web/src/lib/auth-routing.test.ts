import { describe, expect, it } from "vitest";

describe("auth routing", () => {
  it("keeps login and logout public", async () => {
    const { isPublicAuthPath } = await import("./auth-routing");

    expect(isPublicAuthPath("/login")).toBe(true);
    expect(isPublicAuthPath("/login/")).toBe(true);
    expect(isPublicAuthPath("/logout")).toBe(true);
  });

  it("protects dashboard and API paths", async () => {
    const { isPublicAuthPath } = await import("./auth-routing");

    expect(isPublicAuthPath("/")).toBe(false);
    expect(isPublicAuthPath("/api/commands")).toBe(false);
  });

  it("builds a safe login redirect for page requests", async () => {
    const { buildLoginRedirectUrl } = await import("./auth-routing");
    const redirectUrl = buildLoginRedirectUrl(
      new URL("https://ops.example.com/?tab=incidents"),
    );

    expect(redirectUrl.toString()).toBe(
      "https://ops.example.com/login?next=%2F%3Ftab%3Dincidents",
    );
  });

  it("sanitizes next paths to same-origin relative URLs", async () => {
    const { sanitizeNextPath } = await import("./auth-routing");

    expect(sanitizeNextPath("/?tab=incidents")).toBe("/?tab=incidents");
    expect(sanitizeNextPath("https://evil.example.com")).toBe("/");
    expect(sanitizeNextPath("//evil.example.com")).toBe("/");
    expect(sanitizeNextPath(null)).toBe("/");
  });
});
