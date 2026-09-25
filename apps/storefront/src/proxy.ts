import { NextRequest, NextResponse } from "next/server";
import { ACCOUNT_LOCALE_COOKIE, LOCALE_COOKIE, pathLocale, resolveLocale } from "@/i18n/config";

// Every page lives under /{locale}. A URL without one gets a redirect to the shopper's language;
// a URL with one is always honoured, because a locale in the URL is an explicit choice.
export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;
  if (pathLocale(pathname)) return NextResponse.next();

  const locale = resolveLocale({
    accountLocale: request.cookies.get(ACCOUNT_LOCALE_COOKIE)?.value,
    cookieLocale: request.cookies.get(LOCALE_COOKIE)?.value,
    acceptLanguage: request.headers.get("accept-language"),
  });
  const url = request.nextUrl.clone();
  url.pathname = pathname === "/" ? `/${locale}` : `/${locale}${pathname}`;
  const response = NextResponse.redirect(url, 307);
  response.headers.set("Vary", "Accept-Language, Cookie");
  return response;
}

export const config = {
  // Skip API routes, Next internals and files with an extension (icon.svg, robots.txt, images).
  matcher: ["/((?!api/|_next/|.*\\.[\\w]+$).*)"],
};
