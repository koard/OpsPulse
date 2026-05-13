import { NextRequest, NextResponse } from "next/server";

const apiBaseUrl = process.env.API_BASE_URL;

export async function GET(request: NextRequest) {
  if (!apiBaseUrl) {
    return NextResponse.json([], { status: 200 });
  }

  const search = request.nextUrl.search;
  return proxy(`${apiBaseUrl}/api/commands${search}`, { method: "GET" });
}

export async function POST(request: NextRequest) {
  if (!apiBaseUrl) {
    return NextResponse.json({ error: "API_BASE_URL is not configured." }, { status: 503 });
  }

  return proxy(`${apiBaseUrl}/api/commands`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: await request.text(),
  });
}

async function proxy(url: string, init: RequestInit) {
  const response = await fetch(url, init);
  const body = await response.text();

  return new NextResponse(body, {
    status: response.status,
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "application/json",
    },
  });
}
