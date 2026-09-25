import { describe, expect, it } from "vitest";
import { localizedUrls, parseSitemapFile, sitemapIndex } from "./sitemap";

describe("sitemaps", () => {
  it("lists every language version with hreflang alternates", () => {
    const xml = localizedUrls("/products/french-press", "2026-09-25");
    expect(xml.match(/<url>/g)).toHaveLength(4);
    expect(xml).toContain('hreflang="x-default"');
    expect(xml).toContain("/ur/products/french-press");
    expect(xml).toContain("<lastmod>2026-09-25</lastmod>");
  });

  it("escapes XML special characters", () => {
    expect(localizedUrls("/c/a&b")).toContain("/c/a&amp;b");
    expect(sitemapIndex(["products-1.xml"])).toContain("/sitemaps/products-1.xml");
  });

  it("accepts only known file names", () => {
    expect(parseSitemapFile("categories.xml")).toEqual({ kind: "categories" });
    expect(parseSitemapFile("products-12.xml")).toEqual({ kind: "products", page: 12 });
    for (const bad of ["products-0.xml", "products-x.xml", "../etc.xml", "products-1.xml.gz", "products-123456.xml"]) expect(parseSitemapFile(bad)).toBeNull();
  });
});
