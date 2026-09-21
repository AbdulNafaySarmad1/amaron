import * as oidc from "openid-client";
import { NextRequest, NextResponse } from "next/server";
import { appUrl, cookieOptions, loginCookieName, sessionCookieName, sessionTtlSeconds } from "@/lib/auth/config";
import { oidcConfiguration } from "@/lib/auth/oidc";
import { createSession, randomOpaqueValue, takeLoginTransaction } from "@/lib/auth/session";
import { hasAdministrationAccess } from "@/lib/auth/security";

const errorResponse = (detail: string, status = 400) => NextResponse.json({ title: "Sign-in failed", detail }, { status, headers: { "Cache-Control": "no-store" } });
export const runtime = "nodejs";
export async function GET(request: NextRequest) {
  const state = request.nextUrl.searchParams.get("state");
  if (!state || state !== request.cookies.get(loginCookieName)?.value) return errorResponse("The login response could not be verified.");
  const transaction = await takeLoginTransaction(state);
  if (!transaction) return errorResponse("The login attempt has expired.");
  try {
    const baseUrl = appUrl(request.nextUrl.origin);
    const tokens = await oidc.authorizationCodeGrant(await oidcConfiguration(), new URL(request.nextUrl.pathname + request.nextUrl.search, baseUrl), {
      pkceCodeVerifier: transaction.codeVerifier, expectedState: state, expectedNonce: transaction.nonce,
    });
    const claims = tokens.claims();
    if (!claims?.sub || !hasAdministrationAccess(tokens.access_token)) return errorResponse("Your account does not have administration access.", 403);
    const id = await createSession({
      accessToken: tokens.access_token, refreshToken: tokens.refresh_token, idToken: tokens.id_token,
      accessTokenExpiresAt: Date.now() + (tokens.expires_in ?? 60) * 1000, csrfToken: randomOpaqueValue(),
      user: { sub: claims.sub, name: typeof claims.name === "string" ? claims.name : undefined, email: typeof claims.email === "string" ? claims.email : undefined },
    });
    const response = NextResponse.redirect(new URL(transaction.returnTo, baseUrl));
    response.cookies.delete(loginCookieName);
    response.cookies.set(sessionCookieName, id, { ...cookieOptions, maxAge: sessionTtlSeconds });
    response.headers.set("Cache-Control", "no-store");
    return response;
  } catch (error) {
    console.error("Admin OIDC callback rejected", error instanceof Error ? error.message : "Unknown error");
    return errorResponse("The identity response was rejected.");
  }
}
