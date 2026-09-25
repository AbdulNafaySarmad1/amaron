import * as oidc from "openid-client";
import { NextRequest, NextResponse } from "next/server";
import { appUrl, cookieOptions, loginCookieName, sessionCookieName, sessionTtlSeconds } from "@/lib/auth/config";
import { authError } from "@/lib/auth/http";
import { oidcConfiguration } from "@/lib/auth/oidc";
import { createSession, randomOpaqueValue, takeLoginTransaction } from "@/lib/auth/session";
import { ACCOUNT_LOCALE_COOKIE, isLocale, PREFERENCE_MAX_AGE } from "@/i18n/config";

export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  const state = request.nextUrl.searchParams.get("state");
  const stateCookie = request.cookies.get(loginCookieName)?.value;
  if (!state || !stateCookie || state !== stateCookie) return authError("The login response could not be verified.");
  const transaction = await takeLoginTransaction(state);
  if (!transaction) return authError("The login attempt has expired. Please sign in again.");

  try {
    const baseUrl = appUrl(request.nextUrl.origin);
    const callbackUrl = new URL(request.nextUrl.pathname + request.nextUrl.search, baseUrl);
    const tokens = await oidc.authorizationCodeGrant(await oidcConfiguration(), callbackUrl, {
      pkceCodeVerifier: transaction.codeVerifier,
      expectedState: state,
      expectedNonce: transaction.nonce,
    });
    const claims = tokens.claims();
    if (!claims?.sub) return authError("The identity provider did not return a subject.");
    const sessionId = await createSession({
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token,
      idToken: tokens.id_token,
      accessTokenExpiresAt: Date.now() + (tokens.expires_in ?? 60) * 1000,
      csrfToken: randomOpaqueValue(),
      user: {
        sub: claims.sub,
        name: typeof claims.name === "string" ? claims.name : undefined,
        email: typeof claims.email === "string" ? claims.email : undefined,
      },
    });
    const response = NextResponse.redirect(new URL(transaction.returnTo, baseUrl));
    response.cookies.delete(loginCookieName);
    response.cookies.set(sessionCookieName, sessionId, { ...cookieOptions, maxAge: sessionTtlSeconds });
    // The OIDC "locale" claim is the account language preference; it outranks the device cookie on unprefixed URLs.
    const accountLocale = typeof claims.locale === "string" ? claims.locale.toLowerCase().split("-")[0] : undefined;
    if (isLocale(accountLocale)) response.cookies.set(ACCOUNT_LOCALE_COOKIE, accountLocale, { path: "/", sameSite: "lax", secure: cookieOptions.secure, maxAge: PREFERENCE_MAX_AGE });
    response.headers.set("Cache-Control", "no-store");
    return response;
  } catch (error) {
    console.error("OIDC callback failed", error instanceof Error ? { name: error.name, message: error.message } : { name: "UnknownError" });
    return authError("The login response was rejected.");
  }
}
