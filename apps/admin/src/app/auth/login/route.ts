import * as oidc from "openid-client";
import { NextRequest, NextResponse } from "next/server";
import { appUrl, cookieOptions, loginCookieName } from "@/lib/auth/config";
import { oidcConfiguration } from "@/lib/auth/oidc";
import { randomOpaqueValue, saveLoginTransaction } from "@/lib/auth/session";
import { safeReturnPath } from "@/lib/auth/security";

export const runtime = "nodejs";
export async function GET(request: NextRequest) {
  try {
    const state = randomOpaqueValue();
    const nonce = randomOpaqueValue();
    const verifier = oidc.randomPKCECodeVerifier();
    const baseUrl = appUrl(request.nextUrl.origin);
    await saveLoginTransaction(state, { codeVerifier: verifier, nonce, returnTo: safeReturnPath(request.nextUrl.searchParams.get("returnTo")) });
    const url = oidc.buildAuthorizationUrl(await oidcConfiguration(), {
      redirect_uri: `${baseUrl}/auth/callback`, response_type: "code", scope: "openid profile email",
      code_challenge: await oidc.calculatePKCECodeChallenge(verifier), code_challenge_method: "S256", state, nonce,
    });
    const response = NextResponse.redirect(url);
    response.cookies.set(loginCookieName, state, { ...cookieOptions, maxAge: 600 });
    response.headers.set("Cache-Control", "no-store");
    return response;
  } catch (error) {
    console.error("Admin OIDC login failed", error instanceof Error ? error.message : "Unknown error");
    return NextResponse.json({ title: "Sign-in unavailable" }, { status: 503, headers: { "Cache-Control": "no-store" } });
  }
}
