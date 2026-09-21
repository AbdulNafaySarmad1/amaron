import { NextRequest, NextResponse } from "next/server";
import { appUrl, requiredEnv } from "@/lib/auth/config";
import { currentSession, validAccessToken } from "@/lib/auth/session";
import { isAllowedPrivateRequest, isStateChangingMethod, validCsrfToken, validRequestOrigin } from "@/lib/auth/security";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

async function proxy(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const path = `/${(await context.params).path.map(encodeURIComponent).join("/")}`;
  if (!isAllowedPrivateRequest(request.method, path)) return NextResponse.json({ title: "Not found", status: 404 }, { status: 404 });

  const current = await currentSession();
  if (!current) return NextResponse.json({ title: "Unauthorized", status: 401 }, { status: 401 });
  if (isStateChangingMethod(request.method) && (!validRequestOrigin(request.headers.get("origin"), appUrl(request.nextUrl.origin)) || !validCsrfToken(current.session.csrfToken, request.headers.get("x-csrf-token")))) {
    return NextResponse.json({ title: "Forbidden", detail: "Invalid CSRF token.", status: 403 }, { status: 403 });
  }
  const accessToken = await validAccessToken(current.id, current.session);
  if (!accessToken) return NextResponse.json({ title: "Unauthorized", status: 401 }, { status: 401 });

  const headers = new Headers({ Accept: "application/json", Authorization: `Bearer ${accessToken}` });
  const contentType = request.headers.get("content-type");
  const idempotencyKey = request.headers.get("idempotency-key");
  if (contentType) headers.set("Content-Type", contentType);
  if (idempotencyKey) headers.set("Idempotency-Key", idempotencyKey);
  let upstream: Response;
  try {
    upstream = await fetch(`${requiredEnv("API_URL")}/api${path}${request.nextUrl.search}`, {
      method: request.method,
      headers,
      body: isStateChangingMethod(request.method) ? await request.arrayBuffer() : undefined,
      redirect: "manual",
      cache: "no-store",
      signal: AbortSignal.timeout(path === "/checkout/confirm" ? 22_000 : 10_000),
    });
  } catch {
    return NextResponse.json({ title: "Service unavailable", detail: "The request could not be completed. Try again.", status: 503 }, { status: 503, headers: { "Cache-Control": "no-store" } });
  }
  const responseHeaders = new Headers({ "Cache-Control": "no-store" });
  const upstreamContentType = upstream.headers.get("content-type");
  if (upstreamContentType) responseHeaders.set("Content-Type", upstreamContentType);
  return new NextResponse(upstream.body, { status: upstream.status, headers: responseHeaders });
}

export const GET = proxy;
export const POST = proxy;
export const PUT = proxy;
export const DELETE = proxy;
