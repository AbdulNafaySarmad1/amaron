import { defaultLocale, locales } from "@/i18n/config";
import { siteUrl } from "./seo";

/** Product URLs per sitemap file. Google's limit is 50,000; 10,000 keeps each file small and quick to regenerate. */
export const PRODUCTS_PER_SITEMAP = 10_000;

const escapeXml = (value: string) => value.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" })[c]!);

/** One <url> per language, each listing every language version (hreflang) plus x-default. */
export function localizedUrls(path: string, lastModified?: string) {
  const site = siteUrl();
  const href = (locale: string) => escapeXml(`${site}/${locale}${path}`);
  const links = [...locales.map((l) => `<xhtml:link rel="alternate" hreflang="${l}" href="${href(l)}"/>`), `<xhtml:link rel="alternate" hreflang="x-default" href="${href(defaultLocale)}"/>`].join("");
  return locales.map((l) => `<url><loc>${href(l)}</loc>${lastModified ? `<lastmod>${escapeXml(lastModified)}</lastmod>` : ""}${links}</url>`).join("");
}

export const urlset = (body: string) =>
  `<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">${body}</urlset>`;

export const sitemapIndex = (files: string[]) =>
  `<?xml version="1.0" encoding="UTF-8"?><sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">${files.map((f) => `<sitemap><loc>${escapeXml(`${siteUrl()}/sitemaps/${f}`)}</loc></sitemap>`).join("")}</sitemapindex>`;

/** "products-3.xml" -> { kind: "products", page: 3 }; anything unexpected -> null. */
export function parseSitemapFile(file: string): { kind: "categories" } | { kind: "products"; page: number } | null {
  if (file === "categories.xml") return { kind: "categories" };
  const match = /^products-([1-9]\d{0,4})\.xml$/.exec(file);
  return match ? { kind: "products", page: Number(match[1]) } : null;
}

export const xmlResponse = (xml: string) => new Response(xml, { headers: { "Content-Type": "application/xml; charset=utf-8", "Cache-Control": "public, max-age=3600" } });
