import { describe, expect, it } from "vitest";
import { localizePath, parseRegion, pathLocale, regionFromHint, resolveLocale, stripLocale } from "./config";

describe("resolveLocale", () => {
  it("prefers the account preference", () => expect(resolveLocale({ accountLocale: "ur", cookieLocale: "ru", acceptLanguage: "ar" })).toBe("ur"));
  it("then the language cookie", () => expect(resolveLocale({ cookieLocale: "ru", acceptLanguage: "ar" })).toBe("ru"));
  it("then Accept-Language by q-value", () => expect(resolveLocale({ acceptLanguage: "de-DE;q=0.9, ar;q=0.8, ru;q=0.95" })).toBe("ru"));
  it("matches regional tags to their language", () => expect(resolveLocale({ acceptLanguage: "ur-PK,en;q=0.5" })).toBe("ur"));
  it("ignores unsupported and q=0 languages", () => expect(resolveLocale({ acceptLanguage: "fr, ru;q=0" })).toBe("en"));
  it("ignores invalid stored values", () => expect(resolveLocale({ accountLocale: "xx", cookieLocale: "../en", acceptLanguage: null })).toBe("en"));
  // Moscow IP, English browser: language follows the browser, never the location.
  it("never derives language from region", () => expect(resolveLocale({ acceptLanguage: "en-US,en;q=0.9" })).toBe("en"));
});

describe("locale paths", () => {
  it("prefixes app paths", () => {
    expect(localizePath("ur", "/")).toBe("/ur");
    expect(localizePath("ur", "/search?q=lamp")).toBe("/ur/search?q=lamp");
  });
  it("leaves API, external and already-localized paths alone", () => {
    for (const path of ["/api/auth/login?returnTo=/ur", "/api", "https://example.com/x", "//evil.example", "/ru/saved", "#top"]) expect(localizePath("ur", path)).toBe(path);
  });
  it("does not mistake look-alike segments for locales", () => {
    expect(localizePath("en", "/urban")).toBe("/en/urban");
    expect(pathLocale("/english")).toBeNull();
  });
  it("strips the locale segment", () => {
    expect(stripLocale("/ar/products/lamp")).toBe("/products/lamp");
    expect(stripLocale("/ar")).toBe("/");
    expect(stripLocale("/products/lamp")).toBe("/products/lamp");
  });
});

describe("region", () => {
  it("uses the edge hint only for regions we serve", () => {
    expect(regionFromHint("ru")).toEqual({ country: "RU", currency: "RUB" });
    expect(regionFromHint("XX")).toEqual({ country: "US", currency: "USD" });
    expect(regionFromHint(null)).toEqual({ country: "US", currency: "USD" });
  });
  it("parses only well-formed stored regions", () => {
    expect(parseRegion("RU:USD")).toEqual({ country: "RU", currency: "USD" });
    expect(parseRegion("ZZ:USD")).toBeNull();
    expect(parseRegion("RU:<script>")).toBeNull();
  });
});
