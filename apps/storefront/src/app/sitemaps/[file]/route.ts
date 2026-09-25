import { serverGet } from "@/lib/api";
import { categoryPath } from "@/lib/categories";
import { localizedUrls, parseSitemapFile, PRODUCTS_PER_SITEMAP, urlset, xmlResponse } from "@/lib/sitemap";
import type { Category } from "@/lib/types";

// Built per request (the catalog API is not reachable at build time); the upstream fetch is cached for an hour.
export const dynamic = "force-dynamic";

type SitemapPage = { items: Array<{ slug: string; updatedAt: string }>; totalCount: number };

export async function GET(_request: Request, { params }: { params: Promise<{ file: string }> }) {
  const target = parseSitemapFile((await params).file);
  if (!target) return new Response("Not found", { status: 404 });
  if (target.kind === "categories") {
    const categories = await serverGet<Category[]>("/api/catalog/categories", 3600);
    return xmlResponse(urlset(categories.map((c) => localizedUrls(categoryPath(c.slug))).join("")));
  }
  const page = await serverGet<SitemapPage>(`/api/catalog/sitemap/products?page=${target.page}&pageSize=${PRODUCTS_PER_SITEMAP}`, 3600);
  if (!page.items.length) return new Response("Not found", { status: 404 });
  return xmlResponse(urlset(page.items.map((p) => localizedUrls(`/products/${encodeURIComponent(p.slug)}`, p.updatedAt.slice(0, 10))).join("")));
}
