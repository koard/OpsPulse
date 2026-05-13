import { NextRequest, NextResponse } from "next/server";

const apiBaseUrl = process.env.API_BASE_URL;

export async function POST(
  _request: NextRequest,
  context: { params: Promise<{ id: string; action: string }> },
) {
  if (!apiBaseUrl) {
    return NextResponse.json({ error: "API_BASE_URL is not configured." }, { status: 503 });
  }

  const { id, action } = await context.params;
  if (!["acknowledge", "resolve"].includes(action)) {
    return NextResponse.json({ error: "Unsupported incident action." }, { status: 400 });
  }

  const response = await fetch(`${apiBaseUrl}/api/incidents/${id}/${action}`, {
    method: "POST",
  });
  const body = await response.text();

  return new NextResponse(body, {
    status: response.status,
    headers: {
      "Content-Type": response.headers.get("Content-Type") ?? "application/json",
    },
  });
}
