"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ProductGrid } from "@/components/product/product-grid";
import { commerceCopy } from "@/content/commerce";
import { browserRequest } from "@/lib/api";
import type { ProductCardModel } from "@/lib/types";
import { useSavedStore } from "@/store/saved-store";

type Loaded = { key: string; products: ProductCardModel[] } | { key: string; failed: true };

export function SavedList() {
  const ids = useSavedStore((state) => state.ids);
  const hydrated = useSavedStore((state) => state.hydrated);
  const key = ids.join(",");
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!key) return;
    const controller = new AbortController();
    browserRequest<ProductCardModel[]>(`/api/public/products/batch?ids=${encodeURIComponent(key)}`, { signal: controller.signal }, 10_000)
      .then((products) => setLoaded({ key, products }))
      .catch(() => { if (!controller.signal.aborted) setLoaded({ key, failed: true }); });
    return () => controller.abort();
  }, [key, attempt]);

  if (!hydrated) return <p className="t-meta" role="status">Loading saved items…</p>;
  if (!ids.length) {
    return (
      <div className="empty-state">
        <h2 className="t-h3">{commerceCopy.saved.emptyTitle}</h2>
        <p className="t-body">{commerceCopy.saved.emptyBody}</p>
        <Link className="button button--secondary button--medium" href="/search">Start exploring</Link>
      </div>
    );
  }
  const current = loaded?.key === key ? loaded : null;
  if (!current) return <p className="t-meta" role="status">Loading saved items…</p>;
  if ("failed" in current) {
    return (
      <div className="empty-state" role="alert">
        <h2 className="t-h3">Saved items didn&apos;t load</h2>
        <p className="t-body">Your list is safe on this device. Check your connection and try again.</p>
        <button type="button" className="button button--secondary button--medium" onClick={() => setAttempt((x) => x + 1)}>Try again</button>
      </div>
    );
  }
  // Product IDs are kept even when a product is temporarily unavailable, so it reappears when restocked.
  return (
    <>
      {current.products.length < ids.length ? <p className="t-meta notice">{commerceCopy.saved.unavailable}</p> : null}
      <ProductGrid products={current.products} />
    </>
  );
}
