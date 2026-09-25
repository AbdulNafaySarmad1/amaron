"use client";

import { useEffect, useState } from "react";
import { ProductGrid } from "@/components/product/product-grid";
import { useLocale, useT } from "@/components/providers/locale-provider";
import { browserRequest } from "@/lib/api";
import { readRecentlyViewed } from "@/lib/recently-viewed";
import type { ProductCardModel } from "@/lib/types";

/** Appears only when this device has a history; current prices come from the API, never from storage. */
export function RecentlyExplored() {
  const t = useT();
  const locale = useLocale();
  const [products, setProducts] = useState<ProductCardModel[]>([]);
  useEffect(() => {
    const ids = readRecentlyViewed().slice(0, 4);
    if (!ids.length) return;
    const controller = new AbortController();
    browserRequest<ProductCardModel[]>(`/api/public/products/batch?ids=${ids.join(",")}&locale=${locale}`, { signal: controller.signal })
      .then(setProducts)
      .catch(() => { /* optional section: stays hidden */ });
    return () => controller.abort();
  }, [locale]);
  if (!products.length) return null;
  return (
    <section className="home-rail" aria-labelledby="home-recent">
      <header className="home-rail__head"><h2 id="home-recent" className="t-h2">{t.home.recent}</h2></header>
      <ProductGrid products={products} />
    </section>
  );
}
