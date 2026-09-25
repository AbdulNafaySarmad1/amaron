import * as oidc from "openid-client";
import { NextRequest, NextResponse } from "next/server";
import { appUrl, cookieOptions, sessionCookieName } from "@/lib/auth/config";
import { noStore } from "@/lib/auth/http";
import { oidcConfiguration } from "@/lib/auth/oidc";
import { currentSession, deleteSession } from "@/lib/auth/session";
import { validCsrfToken, validRequestOrigin } from "@/lib/auth/security";
import { ACCOUNT_LOCALE_COOKIE, defaultLocale, isLocale } from "@/i18n/config";

export const runtime = "nodejs";

export async function POST(request: NextRequest) {
  // Signed-out shoppers land on the homepage in the language they were browsing.
  const locale = request.nextUrl.searchParams.get("locale");
  const homePath = `/${isLocale(locale) ? locale : defaultLocale}`;
  const current = await currentSession();
  if (!current) return noStore(NextResponse.json({ logoutUrl: homePath }));
  if (!validRequestOrigin(request.headers.get("origin"), appUrl(request.nextUrl.origin)) || !validCsrfToken(current.session.csrfToken, request.headers.get("x-csrf-token"))) {
    return noStore(NextResponse.json({ title: "Forbidden", status: 403 }, { status: 403 }));
  }

  await deleteSession(current.id);
  const home = `${appUrl(request.nextUrl.origin)}${homePath}`;
  let logoutUrl = home;
  try {
    logoutUrl = oidc.buildEndSessionUrl(await oidcConfiguration(), {
      post_logout_redirect_uri: home,
      ...(current.session.idToken ? { id_token_hint: current.session.idToken } : {}),
    }).href;
  } catch (error) {
    console.error("Unable to build RP-initiated logout URL", error instanceof Error ? error.name : "UnknownError");
  }
  const response = NextResponse.json({ logoutUrl });
  response.cookies.set(sessionCookieName, "", { ...cookieOptions, maxAge: 0 });
  response.cookies.delete(ACCOUNT_LOCALE_COOKIE);
  return noStore(response);
}
