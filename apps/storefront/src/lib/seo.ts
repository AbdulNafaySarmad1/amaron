import { defaultLocale, type Locale, locales } from "@/i18n/config";
import type { ProductDetail } from "./types";

/** Canonical origin, no trailing slash. APP_URL is required in production (see lib/auth/config). */
export function siteUrl() {
  return (process.env.APP_URL ?? "http://localhost:3000").replace(/\/+$/, "");
}

/** Canonical URL for this language plus hreflang alternates for every language, with English as x-default. */
export function alternates(locale: Locale, path: string) {
  const site = siteUrl();
  const url = (l: Locale) => `${site}/${l}${path === "/" ? "" : path}`;
  return {
    canonical: url(locale),
    languages: { ...Object.fromEntries(locales.map((l) => [l, url(l)])), "x-default": url(defaultLocale) },
  };
}

/** JSON for a <script type="application/ld+json">: escapes characters that could close the tag or start markup. */
// Line and paragraph separators are valid JSON but end a line in older JavaScript parsers.
const LINE_SEPARATOR = new RegExp(String.fromCharCode(0x2028), "g");
const PARAGRAPH_SEPARATOR = new RegExp(String.fromCharCode(0x2029), "g");

export function safeJsonLd(data: unknown) {
  return JSON.stringify(data)
    .replace(/</g, "\\u003c")
    .replace(/>/g, "\\u003e")
    .replace(/&/g, "\\u0026")
    .replace(LINE_SEPARATOR, "\\u2028")
    .replace(PARAGRAPH_SEPARATOR, "\\u2029");
}

const availability: Record<string, string> = {
  in_stock: "https://schema.org/InStock",
  low_stock: "https://schema.org/LimitedAvailability",
  out_of_stock: "https://schema.org/OutOfStock",
};

/**
 * schema.org Product built from the same API response the page renders, so structured data always matches what the
 * shopper sees. One variant is an Offer; several are an AggregateOffer. Ratings appear only when reviews exist.
 * No image: seed assets are placeholders, and advertising images that do not exist would mislead crawlers.
 */
export function productJsonLd(product: ProductDetail, url: string) {
  const variants = product.variants;
  const prices = variants.map((v) => v.price.amount);
  const currency = variants[0]?.price.currency;
  const best = variants.some((v) => v.availabilityHint === "in_stock") ? "in_stock" : variants.some((v) => v.availabilityHint === "low_stock") ? "low_stock" : "out_of_stock";
  const offers = variants.length === 1
    ? { "@type": "Offer", url, price: prices[0]!.toFixed(2), priceCurrency: currency, availability: availability[variants[0]!.availabilityHint] ?? availability.out_of_stock, sku: variants[0]!.sku }
    : { "@type": "AggregateOffer", url, lowPrice: Math.min(...prices).toFixed(2), highPrice: Math.max(...prices).toFixed(2), offerCount: variants.length, priceCurrency: currency, availability: availability[best] };
  return {
    "@context": "https://schema.org",
    "@type": "Product",
    name: product.title,
    description: product.description,
    sku: variants.length === 1 ? variants[0]!.sku : undefined,
    brand: { "@type": "Brand", name: product.brand },
    category: product.category,
    inLanguage: product.locale,
    additionalProperty: product.specifications.map((spec) => ({ "@type": "PropertyValue", name: spec.label, value: spec.value })),
    ...(variants.length ? { offers } : {}),
    ...(product.reviewCount > 0 ? { aggregateRating: { "@type": "AggregateRating", ratingValue: product.rating.toFixed(1), reviewCount: product.reviewCount, bestRating: "5", worstRating: "1" } } : {}),
  };
}

export function breadcrumbJsonLd(items: Array<{ name: string; url: string }>) {
  return {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: items.map((item, index) => ({ "@type": "ListItem", position: index + 1, name: item.name, item: item.url })),
  };
}

export function organizationJsonLd(locale: Locale) {
  const site = siteUrl();
  return [
    { "@context": "https://schema.org", "@type": "Organization", name: "Amaron", url: `${site}/${locale}` },
    {
      "@context": "https://schema.org",
      "@type": "WebSite",
      name: "Amaron",
      url: `${site}/${locale}`,
      inLanguage: locale,
      potentialAction: { "@type": "SearchAction", target: { "@type": "EntryPoint", urlTemplate: `${site}/${locale}/search?q={search_term_string}` }, "query-input": "required name=search_term_string" },
    },
  ];
}
