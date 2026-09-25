"use client";

import { useEffect, useState } from "react";
import { Link, useIntlLocale, useLocale, useT } from "@/components/providers/locale-provider";
import { ProductVisual } from "@/components/ui/product-visual";
import { CATALOG_LANG } from "@/i18n/config";
import { formatMoney } from "@/lib/api";
import type { RelatedProduct } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";

/** One suggestion at most, with its reason. The bag is for finishing a purchase, not another product feed. */
export function UsefulAddition({ productIds, onNavigate }: { productIds: string[]; onNavigate: () => void }) {
  const t = useT();
  const intl = useIntlLocale();
  const locale = useLocale();
  const add = useCartStore((state) => state.add);
  const isLoading = useCartStore((state) => state.isLoading);
  const key = [...new Set(productIds)].sort().join(",");
  const [suggestion, setSuggestion] = useState<{ key: string; item: RelatedProduct | null } | null>(null);

  useEffect(() => {
    if (!key) return;
    const controller = new AbortController();
    // Plain fetch: "nothing to suggest" is a 204 with no body.
    fetch(`/api/public/products/addition?ids=${key}&locale=${locale}`, { signal: controller.signal, headers: { Accept: "application/json" } })
      .then(async (response) => setSuggestion({ key, item: response.status === 200 ? await response.json() as RelatedProduct : null }))
      .catch(() => { /* optional: the bag works without it */ });
    return () => controller.abort();
  }, [key, locale]);

  const item = suggestion?.key === key ? suggestion.item : null;
  if (!item) return null;
  const { product } = item;
  return (
    <aside className="useful-addition" aria-labelledby="useful-addition-title">
      <p id="useful-addition-title" className="t-caption">{t.related.usefulAddition}</p>
      <div className="useful-addition__row">
        <Link className="useful-addition__visual" href={`/products/${product.slug}`} onClick={onNavigate} tabIndex={-1} aria-hidden="true"><ProductVisual compact slug={product.slug} title={product.title} /></Link>
        <div>
          <Link href={`/products/${product.slug}`} onClick={onNavigate}><strong lang={product.locale} dir="auto">{product.title}</strong></Link>
          <p className="t-meta" lang={CATALOG_LANG} dir="auto">{item.reason}</p>
          <p className="t-meta">{formatMoney(product.price, intl)}</p>
        </div>
        <button type="button" className="button button--secondary button--small" disabled={isLoading} onClick={() => void add(product.defaultVariantId)}>{t.related.add}</button>
      </div>
    </aside>
  );
}

