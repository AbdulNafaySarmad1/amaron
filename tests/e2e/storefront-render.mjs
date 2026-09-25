// Server-rendered locale, RTL, SEO and header checks against a running storefront: node tests/e2e/storefront-render.mjs
// Reads the HTML as a crawler would (no JavaScript), so everything checked here is present before hydration.
const WEB = process.env.APP_URL ?? "http://127.0.0.1:3000";
let failures = 0;
const check = (name, ok, detail = "") => { console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? `  (${detail})` : ""}`); if (!ok) failures++; };
const get = async (path, headers = {}) => { const r = await fetch(WEB + path, { headers, redirect: "manual" }); return { status: r.status, headers: r.headers, html: await r.text() }; };
const attr = (html, re) => html.match(re)?.[1];

const expectations = { en: ["ltr", "Search"], ru: ["ltr", "Поиск"], ur: ["rtl", "تلاش"], ar: ["rtl", "بحث"] };
for (const [locale, [dir]] of Object.entries(expectations)) {
  const { html } = await get(`/${locale}/c/laptops`);
  check(`${locale}: <html lang dir> rendered on the server`, attr(html, /<html[^>]* lang="([^"]+)"/) === locale && attr(html, /<html[^>]* dir="([^"]+)"/) === dir);
  const hreflangs = [...html.matchAll(/hrefLang="([^"]+)"/g)].map((m) => m[1]).sort().join(",");
  check(`${locale}: canonical and all hreflang alternates`, attr(html, /<link rel="canonical" href="([^"]+)"/)?.endsWith(`/${locale}/c/laptops`) && hreflangs === "ar,en,ru,ur,x-default", hreflangs);
  check(`${locale}: title and description in <head>`, /<title>[^<]+<\/title>/.test(html) && /<meta name="description" content="[^"]+"/.test(html));
}

// English catalog data inside RTL pages is marked, so it is read and shaped as English.
const ur = (await get("/ur/c/laptops")).html;
const titles = [...ur.matchAll(/<h3 class="t-product"[^>]*>/g)].map((m) => m[0]);
check("ur: untranslated product titles carry lang=en", titles.length > 0 && titles.every((t) => /lang="en"/.test(t)), `${titles.length} cards`);
check("ur: no leftover 'untranslated' English blocks on catalog pages", !/class="untranslated"/.test(ur) && !/class="untranslated"/.test((await get("/ur/search")).html));

// Region and language are independent: Urdu page, Russian region.
const mixed = (await get("/ur/c/laptops", { cookie: "amaron-region=RU:USD" })).html;
check("region is independent of language (ur page, RU region)", attr(mixed, /<html[^>]* lang="([^"]+)"/) === "ur" && /Russia|Россия|روس/.test(mixed));

// Locale negotiation and unknown locales
const root = await get("/", { "accept-language": "ar,en;q=0.5" });
check("root redirects by Accept-Language", root.status >= 300 && root.status < 400 && /\/ar/.test(root.headers.get("location") ?? ""), root.headers.get("location"));
const unknownLocale = await get("/xx/c/laptops");
check("unknown locale is treated as a path under the default locale, then 404", unknownLocale.status === 307 && (await get(unknownLocale.headers.get("location").replace(WEB, ""))).status === 404);
// Streamed pages cannot change status after headers are sent (Next.js loading.tsx); a missing resource renders the
// not-found page marked noindex, which is what keeps it out of search indexes.
for (const path of ["/en/c/no-such-space", "/en/products/no-such-product"]) { const r = await get(path); check(`${path} renders not-found, noindex`, r.html.includes(`<meta name="robots" content="noindex"`)); }

// Security headers
const { headers } = await get("/en");
const csp = headers.get("content-security-policy") ?? "";
check("CSP: no third-party script origins unless configured", /script-src 'self' 'unsafe-inline'(;|$)/.test(csp) && /frame-ancestors 'none'/.test(csp) && /object-src 'none'/.test(csp));
check("nosniff, referrer policy, COOP, permissions policy", headers.get("x-content-type-options") === "nosniff" && !!headers.get("referrer-policy") && headers.get("cross-origin-opener-policy") === "same-origin" && !!headers.get("permissions-policy"));
check("no X-Powered-By", !headers.get("x-powered-by"));

console.log(failures ? `\n${failures} FAILED` : "\nall passed");
process.exitCode = failures ? 1 : 0;
