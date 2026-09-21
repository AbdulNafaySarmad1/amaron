import * as oidc from "openid-client";
import { NextRequest, NextResponse } from "next/server";
import { appUrl, cookieOptions, loginCookieName } from "@/lib/auth/config";
import { authError } from "@/lib/auth/http";
import { oidcConfiguration } from "@/lib/auth/oidc";
import { randomOpaqueValue, saveLoginTransaction } from "@/lib/auth/session";
import { safeReturnPath } from "@/lib/auth/security";

export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  try {
    const state = randomOpaqueValue();
    const nonce = randomOpaqueValue();
    const codeVerifier = oidc.randomPKCECodeVerifier();
    const codeChallenge = await oidc.calculatePKCECodeChallenge(codeVerifier);
    const baseUrl = appUrl(request.nextUrl.origin);
    await saveLoginTransaction(state, { codeVerifier, nonce, returnTo: safeReturnPath(request.nextUrl.searchParams.get("returnTo")) });
    const authorizationUrl = oidc.buildAuthorizationUrl(await oidcConfiguration(), {
      redirect_uri: `${baseUrl}/api/auth/callback`,
      response_type: "code",
      scope: "openid profile email",
      code_challenge: codeChallenge,
      code_challenge_method: "S256",
      state,
      nonce,
    });
    const response = NextResponse.redirect(authorizationUrl);
    response.cookies.set(loginCookieName, state, { ...cookieOptions, maxAge: 600 });
    response.headers.set("Cache-Control", "no-store");
    return response;
  } catch (error) {
    console.error("Unable to start OIDC login", error instanceof Error ? { name: error.name, message: error.message } : { name: "UnknownError" });
    return authError("Login is temporarily unavailable.", 503);
  }
}
