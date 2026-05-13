import { NextResponse } from "next/server";

const apiBaseUrl = process.env.API_BASE_URL;

export async function GET() {
  if (!apiBaseUrl) {
    return NextResponse.json([], { status: 200 });
  }

  return proxy(`${apiBaseUrl}/api/repositories`, { method: "GET" });
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
