export function isPublicAuthPath(pathname: string): boolean {
  return pathname === "/login" || pathname === "/login/" || pathname === "/logout";
}

export function buildLoginRedirectUrl(requestUrl: URL): URL {
  const redirectUrl = new URL("/login", requestUrl);
  const nextPath = `${requestUrl.pathname}${requestUrl.search}`;

  redirectUrl.searchParams.set("next", sanitizeNextPath(nextPath));

  return redirectUrl;
}

export function sanitizeNextPath(value: unknown): string {
  if (typeof value !== "string" || !value.startsWith("/") || value.startsWith("//")) {
    return "/";
  }

  return value;
}
