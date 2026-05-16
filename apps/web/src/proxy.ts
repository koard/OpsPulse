import { NextRequest, NextResponse } from "next/server";
import { AUTH_COOKIE_NAME, verifySessionToken } from "@/lib/auth";
import { buildLoginRedirectUrl, isPublicAuthPath } from "@/lib/auth-routing";

export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (isPublicAuthPath(pathname)) {
    return NextResponse.next();
  }

  const session = request.cookies.get(AUTH_COOKIE_NAME)?.value;
  if (verifySessionToken(session)) {
    return NextResponse.next();
  }

  if (pathname.startsWith("/api/")) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  return NextResponse.redirect(buildLoginRedirectUrl(request.nextUrl));
}

export const config = {
  matcher: [
    "/((?!_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|css|js|map|txt|xml|webmanifest)$).*)",
  ],
};
