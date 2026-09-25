import { NextRequest, NextResponse } from "next/server";
import { requiredEnv } from "@/lib/auth/config";

export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.searchParams.get("q")?.trim() ?? "";
  const category = request.nextUrl.searchParams.get("category")?.trim() ?? "";
  if (query.length < 2 || query.length > 200) return NextResponse.json([]);
  if (category && !/^[a-z0-9-]{1,160}$/.test(category)) return NextResponse.json([]);
  const upstream = await fetch(`${requiredEnv("API_URL")}/api/search/suggestions?q=${encodeURIComponent(query)}${category ? `&category=${category}` : ""}`, {
    headers: { Accept: "application/json" },
    signal: AbortSignal.timeout(8_000),
  });
  return new NextResponse(upstream.body, {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json", "Cache-Control": "private, max-age=15" },
  });
}
