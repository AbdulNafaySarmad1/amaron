import { serverGet } from "@/lib/api";
import { PRODUCTS_PER_SITEMAP, sitemapIndex, xmlResponse } from "@/lib/sitemap";

// Built per request (the catalog API is not reachable at build time); the upstream fetch is cached for an hour.
export const dynamic = "force-dynamic";

/** Sitemap index: one categories file plus as many product files as the catalog needs. */
export async function GET() {
  const { totalCount } = await serverGet<{ totalCount: number }>("/api/catalog/sitemap/products?page=1&pageSize=1", 3600);
  const productFiles = Array.from({ length: Math.max(1, Math.ceil(totalCount / PRODUCTS_PER_SITEMAP)) }, (_, i) => `products-${i + 1}.xml`);
  return xmlResponse(sitemapIndex(["categories.xml", ...productFiles]));
}
