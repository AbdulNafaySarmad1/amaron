"use client";

import { useEffect, useState } from "react";
import { ProductGrid } from "@/components/product/product-grid";
import { Link, useLocale, useT } from "@/components/providers/locale-provider";
import { browserRequest } from "@/lib/api";
import type { ProductCardModel } from "@/lib/types";
import { useSavedStore } from "@/store/saved-store";

type Loaded = { key: string; products: ProductCardModel[] } | { key: string; failed: true };

export function SavedList() {
  const t = useT();
  const locale = useLocale();
  const ids = useSavedStore((state) => state.ids);
  const hydrated = useSavedStore((state) => state.hydrated);
  const key = ids.join(",");
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!key) return;
    const controller = new AbortController();
    browserRequest<ProductCardModel[]>(`/api/public/products/batch?ids=${encodeURIComponent(key)}&locale=${locale}`, { signal: controller.signal }, 10_000)
      .then((products) => setLoaded({ key, products }))
      .catch(() => { if (!controller.signal.aborted) setLoaded({ key, failed: true }); });
    return () => controller.abort();
  }, [key, attempt, locale]);

  if (!hydrated) return <p className="t-meta" role="status">{t.saved.loading}</p>;
  if (!ids.length) {
    return (
      <div className="empty-state">
        <h2 className="t-h3">{t.saved.emptyTitle}</h2>
        <p className="t-body">{t.saved.emptyBody}</p>
        <Link className="button button--secondary button--medium" href="/search">{t.cart.startShopping}</Link>
      </div>
    );
  }
  const current = loaded?.key === key ? loaded : null;
  if (!current) return <p className="t-meta" role="status">{t.saved.loading}</p>;
  if ("failed" in current) {
    return (
      <div className="empty-state" role="alert">
        <h2 className="t-h3">{t.saved.failedTitle}</h2>
        <p className="t-body">{t.saved.failedBody}</p>
        <button type="button" className="button button--secondary button--medium" onClick={() => setAttempt((x) => x + 1)}>{t.saved.retry}</button>
      </div>
    );
  }
  // Product IDs are kept even when a product is temporarily unavailable, so it reappears when restocked.
  return (
    <>
      {current.products.length < ids.length ? <p className="t-meta notice">{t.saved.unavailable}</p> : null}
      <ProductGrid products={current.products} />
    </>
  );
}
