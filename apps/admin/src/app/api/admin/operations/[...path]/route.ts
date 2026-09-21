import { NextRequest, NextResponse } from "next/server";
import { appUrl, requiredEnv } from "@/lib/auth/config";
import { currentSession, validAccessToken } from "@/lib/auth/session";
import { isAllowedOperation, isStateChangingMethod, validCsrfToken, validRequestOrigin } from "@/lib/auth/security";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

async function proxy(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const segments = (await context.params).path;
  const path = `/${segments.map(encodeURIComponent).join("/")}`;
  if (!isAllowedOperation(request.method, path)) return NextResponse.json({ title: "Not found", status: 404 }, { status: 404 });
  const current = await currentSession();
  if (!current) return NextResponse.json({ title: "Unauthorized", status: 401 }, { status: 401, headers: { "Cache-Control": "no-store" } });
  if (isStateChangingMethod(request.method) && (!validRequestOrigin(request.headers.get("origin"), appUrl(request.nextUrl.origin)) || !validCsrfToken(current.session.csrfToken, request.headers.get("x-csrf-token")))) {
    return NextResponse.json({ title: "Forbidden", detail: "Request verification failed.", status: 403 }, { status: 403, headers: { "Cache-Control": "no-store" } });
  }
  const token = await validAccessToken(current.id, current.session);
  if (!token) return NextResponse.json({ title: "Unauthorized", status: 401 }, { status: 401, headers: { "Cache-Control": "no-store" } });
  const headers = new Headers({ Accept: "application/json", Authorization: `Bearer ${token}` });
  const contentType = request.headers.get("content-type");
  const idempotencyKey = request.headers.get("idempotency-key");
  if (contentType) headers.set("Content-Type", contentType);
  if (idempotencyKey) headers.set("Idempotency-Key", idempotencyKey);
  try {
    const upstream = await fetch(`${requiredEnv("API_URL")}/api/admin/operations${path}${request.nextUrl.search}`, {
      method: request.method, headers, body: isStateChangingMethod(request.method) ? await request.arrayBuffer() : undefined,
      redirect: "manual", cache: "no-store", signal: AbortSignal.timeout(15_000),
    });
    const responseHeaders = new Headers({ "Cache-Control": "no-store" });
    const upstreamType = upstream.headers.get("content-type");
    if (upstreamType) responseHeaders.set("Content-Type", upstreamType);
    return new NextResponse(upstream.body, { status: upstream.status, headers: responseHeaders });
  } catch {
    return NextResponse.json({ title: "Service unavailable", detail: "The operations service could not be reached.", status: 503 }, { status: 503, headers: { "Cache-Control": "no-store" } });
  }
}

export const GET = proxy;
export const POST = proxy;
