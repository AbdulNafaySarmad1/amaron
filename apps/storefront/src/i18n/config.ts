// Language, region and currency are separate preferences. None of them is derived from another,
// and IP geolocation is only ever a hint for the first-visit region default.

export const locales = ["en", "ru", "ur", "ar"] as const;
export type Locale = (typeof locales)[number];
export const defaultLocale: Locale = "en";

export const localeNames: Record<Locale, string> = { en: "English", ru: "Русский", ur: "اردو", ar: "العربية" };
/** BCP 47 tags for Intl formatting. */
export const intlLocale: Record<Locale, string> = { en: "en-US", ru: "ru-RU", ur: "ur-PK", ar: "ar" };
const rtl = new Set<Locale>(["ur", "ar"]);

export const LOCALE_COOKIE = "amaron-locale";
export const ACCOUNT_LOCALE_COOKIE = "amaron-account-locale";
export const REGION_COOKIE = "amaron-region";
export const PREFERENCE_MAX_AGE = 60 * 60 * 24 * 365;

export const isLocale = (value: string | null | undefined): value is Locale => !!value && (locales as readonly string[]).includes(value);
export const localeDirection = (locale: Locale) => (rtl.has(locale) ? "rtl" : "ltr");

/** Locale for a request whose URL carries none. Order: account preference, cookie, Accept-Language, default. */
export function resolveLocale({ accountLocale, cookieLocale, acceptLanguage }: { accountLocale?: string | null; cookieLocale?: string | null; acceptLanguage?: string | null }): Locale {
  if (isLocale(accountLocale)) return accountLocale;
  if (isLocale(cookieLocale)) return cookieLocale;
  const ranked = (acceptLanguage ?? "")
    .split(",")
    .map((part, index) => {
      const [tag = "", ...params] = part.trim().split(";");
      const q = Number(params.find((p) => p.trim().startsWith("q="))?.trim().slice(2) ?? 1);
      return { language: tag.toLowerCase().split("-")[0] ?? "", q: Number.isFinite(q) ? q : 0, index };
    })
    .filter((x) => x.language && x.q > 0)
    .sort((a, b) => b.q - a.q || a.index - b.index);
  return ranked.map((x) => x.language).find(isLocale) ?? defaultLocale;
}

/** The locale segment of a pathname, if it has one. */
export function pathLocale(pathname: string): Locale | null {
  const segment = pathname.split("/")[1];
  return isLocale(segment) ? segment : null;
}

/** Removes the locale segment: "/ur/saved" -> "/saved", "/ur" -> "/". */
export function stripLocale(pathname: string): string {
  const locale = pathLocale(pathname);
  if (!locale) return pathname;
  return pathname.slice(locale.length + 1) || "/";
}

/** Prefixes app paths with a locale. API routes, external URLs and already-localized paths pass through. */
export function localizePath(locale: Locale, path: string): string {
  if (!path.startsWith("/") || path.startsWith("//") || path === "/api" || path.startsWith("/api/") || path.startsWith("/api?") || pathLocale(path)) return path;
  return path === "/" ? `/${locale}` : `/${locale}${path}`;
}

// ---- Region ----

export type Region = { country: string; currency: string };
export const regions: Record<string, { name: string; currency: string }> = {
  PK: { name: "Pakistan", currency: "PKR" },
  RU: { name: "Russia", currency: "RUB" },
  AE: { name: "United Arab Emirates", currency: "AED" },
  SA: { name: "Saudi Arabia", currency: "SAR" },
  GB: { name: "United Kingdom", currency: "GBP" },
  DE: { name: "Germany", currency: "EUR" },
  US: { name: "United States", currency: "USD" },
};
export const defaultRegion: Region = { country: "US", currency: "USD" };
/** Currencies the pricing service can charge in today. The catalog is priced in USD; others need server-side FX first. */
export const chargeableCurrencies = ["USD"] as const;

export function parseRegion(raw: string | null | undefined): Region | null {
  const [country, currency] = (raw ?? "").split(":");
  return country && country in regions && currency && /^[A-Z]{3}$/.test(currency) ? { country, currency } : null;
}
export const serializeRegion = (region: Region) => `${region.country}:${region.currency}`;

/** Region for a first visit: the edge country hint if we ship there, else the default. Never binding. */
export function regionFromHint(countryHint: string | null | undefined): Region {
  const country = countryHint?.toUpperCase();
  return country && country in regions ? { country, currency: regions[country]!.currency } : defaultRegion;
}

/** Language of catalog content (names, descriptions) until product translations ship; the API will then report it per field. */
export const CATALOG_LANG = "en";

/** Adds the content locale to a catalog API path so names and descriptions come back translated where available. */
export const withLocale = (path: string, locale: Locale) => `${path}${path.includes("?") ? "&" : "?"}locale=${locale}`;
