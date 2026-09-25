import type { MetadataRoute } from "next";
import { siteUrl } from "@/lib/seo";

/** Catalog pages are open to crawlers; personal and transactional pages are not. */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/api/", "/*/checkout", "/*/orders", "/*/saved"] },
    sitemap: `${siteUrl()}/sitemap.xml`,
  };
}
