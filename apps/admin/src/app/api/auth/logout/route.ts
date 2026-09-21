import * as oidc from "openid-client";
import { NextRequest, NextResponse } from "next/server";
import { appUrl, cookieOptions, sessionCookieName } from "@/lib/auth/config";
import { oidcConfiguration } from "@/lib/auth/oidc";
import { currentSession, deleteSession } from "@/lib/auth/session";
import { validCsrfToken, validRequestOrigin } from "@/lib/auth/security";

export const runtime = "nodejs";
export async function POST(request: NextRequest) {
  const current = await currentSession();
  if (!current) return NextResponse.json({ logoutUrl: "/" }, { headers: { "Cache-Control": "no-store" } });
  if (!validRequestOrigin(request.headers.get("origin"), appUrl(request.nextUrl.origin)) || !validCsrfToken(current.session.csrfToken, request.headers.get("x-csrf-token"))) {
    return NextResponse.json({ title: "Forbidden" }, { status: 403, headers: { "Cache-Control": "no-store" } });
  }
  await deleteSession(current.id);
  const baseUrl = appUrl(request.nextUrl.origin);
  let logoutUrl = baseUrl;
  try {
    logoutUrl = oidc.buildEndSessionUrl(await oidcConfiguration(), { post_logout_redirect_uri: baseUrl, ...(current.session.idToken ? { id_token_hint: current.session.idToken } : {}) }).href;
  } catch (error) { console.error("Unable to build admin logout URL", error instanceof Error ? error.name : "Unknown error"); }
  const response = NextResponse.json({ logoutUrl });
  response.cookies.set(sessionCookieName, "", { ...cookieOptions, maxAge: 0 });
  response.headers.set("Cache-Control", "no-store");
  return response;
}
