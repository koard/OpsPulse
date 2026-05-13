import { NextResponse } from "next/server";

const apiBaseUrl = process.env.API_BASE_URL;

export async function POST(
  _request: Request,
  context: { params: Promise<{ id: string }> },
) {
  if (!apiBaseUrl) {
    return NextResponse.json({ error: "API_BASE_URL is not configured." }, { status: 503 });
  }

  const { id } = await context.params;
  const response = await fetch(`${apiBaseUrl}/api/repositories/${encodeURIComponent(id)}/sync`, {
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
