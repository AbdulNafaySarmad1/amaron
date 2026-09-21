import * as oidc from "openid-client";
import { NextRequest, NextResponse } from "next/server";
import { appUrl, cookieOptions, sessionCookieName } from "@/lib/auth/config";
import { noStore } from "@/lib/auth/http";
import { oidcConfiguration } from "@/lib/auth/oidc";
import { currentSession, deleteSession } from "@/lib/auth/session";
import { validCsrfToken, validRequestOrigin } from "@/lib/auth/security";

export const runtime = "nodejs";

export async function POST(request: NextRequest) {
  const current = await currentSession();
  if (!current) return noStore(NextResponse.json({ logoutUrl: "/" }));
  if (!validRequestOrigin(request.headers.get("origin"), appUrl(request.nextUrl.origin)) || !validCsrfToken(current.session.csrfToken, request.headers.get("x-csrf-token"))) {
    return noStore(NextResponse.json({ title: "Forbidden", status: 403 }, { status: 403 }));
  }

  await deleteSession(current.id);
  const baseUrl = appUrl(request.nextUrl.origin);
  let logoutUrl = `${baseUrl}/`;
  try {
    logoutUrl = oidc.buildEndSessionUrl(await oidcConfiguration(), {
      post_logout_redirect_uri: `${baseUrl}/`,
      ...(current.session.idToken ? { id_token_hint: current.session.idToken } : {}),
    }).href;
  } catch (error) {
    console.error("Unable to build RP-initiated logout URL", error instanceof Error ? error.name : "UnknownError");
  }
  const response = NextResponse.json({ logoutUrl });
  response.cookies.set(sessionCookieName, "", { ...cookieOptions, maxAge: 0 });
  return noStore(response);
}
