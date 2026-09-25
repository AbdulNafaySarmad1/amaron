import { describe, expect, it } from "vitest";
import { alternates, productJsonLd, safeJsonLd } from "./seo";
import type { ProductDetail } from "./types";

const base: ProductDetail = {
  id: "p", slug: "french-press", title: "French Press", brand: "Harbor", description: "Makes coffee.", category: "Home & Kitchen", categorySlug: "home-kitchen",
  categoryLocale: "en", locale: "en", seoTitle: null, seoDescription: null, kind: "coffee", specifications: [{ label: "Capacity", value: "1 l" }],
  variants: [{ id: "v", sku: "AM-0021", name: "Standard", price: { amount: 59.24, currency: "USD" }, listPrice: null, availabilityHint: "low_stock" }],
  assets: [], rating: 4.26, reviewCount: 7,
};

describe("seo", () => {
  it("gives every language an alternate and English as x-default", () => {
    const result = alternates("ur", "/products/french-press");
    expect(result.canonical).toMatch(/\/ur\/products\/french-press$/);
    expect(Object.keys(result.languages)).toEqual(["en", "ru", "ur", "ar", "x-default"]);
    expect(result.languages["x-default"]).toMatch(/\/en\/products\/french-press$/);
    expect(alternates("en", "/").canonical).toMatch(/\/en$/);
  });

  it("builds Product data from exactly the values the page shows", () => {
    const data = productJsonLd(base, "https://shop.example/en/products/french-press") as Record<string, unknown>;
    expect(data.offers).toMatchObject({ "@type": "Offer", price: "59.24", priceCurrency: "USD", availability: "https://schema.org/LimitedAvailability", sku: "AM-0021" });
    expect(data.aggregateRating).toMatchObject({ ratingValue: "4.3", reviewCount: 7 });
    expect(data.additionalProperty).toEqual([{ "@type": "PropertyValue", name: "Capacity", value: "1 l" }]);
  });

  it("uses an AggregateOffer for several variants and omits ratings without reviews", () => {
    const data = productJsonLd({ ...base, reviewCount: 0, variants: [...base.variants, { ...base.variants[0]!, id: "w", price: { amount: 79, currency: "USD" }, availabilityHint: "in_stock" }] }, "u") as Record<string, unknown>;
    expect(data.offers).toMatchObject({ "@type": "AggregateOffer", lowPrice: "59.24", highPrice: "79.00", offerCount: 2, availability: "https://schema.org/InStock" });
    expect(data.aggregateRating).toBeUndefined();
  });

  it("cannot be broken out of by catalog text", () => {
    const json = safeJsonLd({ name: "</script><script>alert(1)</script> & co" });
    expect(json).not.toContain("<");
    expect(json).not.toContain(">");
    expect(JSON.parse(json).name).toBe("</script><script>alert(1)</script> & co");
  });
});
