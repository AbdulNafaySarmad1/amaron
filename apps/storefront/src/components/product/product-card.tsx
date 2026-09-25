"use client";

import { useState } from "react";
import { HeartIcon, StarIcon } from "@/components/icons";
import { Link, useIntlLocale, useT } from "@/components/providers/locale-provider";
import { Button } from "@/components/ui/button";
import { ProductVisual } from "@/components/ui/product-visual";
import { CATALOG_LANG } from "@/i18n/config";
import { format, plural } from "@/i18n/dictionary";
import { formatMoney } from "@/lib/api";
import { specText } from "@/lib/specs";
import type { ProductCardModel } from "@/lib/types";
import { useSavedStore } from "@/store/saved-store";

type AddState = "idle" | "loading" | "success" | "error";

/** Object-first card: the product carries the layout, and its kind decides the shape (books get a cover). */
export function ProductCard({ product, onAdd, priority = false }: { product: ProductCardModel; onAdd?: (variantId: string) => Promise<boolean | void>; priority?: boolean }) {
  const t = useT();
  const intl = useIntlLocale();
  const [addState, setAddState] = useState<AddState>("idle");
  const saved = useSavedStore((state) => state.ids.includes(product.id));
  const toggleSaved = useSavedStore((state) => state.toggle);
  const unavailable = product.availabilityHint === "out_of_stock";
  const isBook = product.kind === "book";

  async function addToCart() {
    if (!onAdd || unavailable || addState === "loading") return;
    setAddState("loading");
    try {
      if (await onAdd(product.defaultVariantId) === false) { setAddState("idle"); return; }
      setAddState("success");
      window.setTimeout(() => setAddState("idle"), 1400);
    } catch {
      setAddState("error");
      window.setTimeout(() => setAddState("idle"), 2200);
    }
  }

  const label = unavailable ? t.addToCart.unavailable
    : addState === "loading" ? t.addToCart.loading
    : addState === "success" ? t.addToCart.success
    : addState === "error" ? t.addToCart.error : t.addToCart.idle;

  return (
    <article className={`product-card product-card--${isBook ? "book" : "object"}`} data-priority={priority || undefined}>
      <div className="product-card__media">
        <Link href={`/products/${product.slug}`} prefetch={false} className="product-card__image-link" aria-label={format(t.product.view, { title: product.title })}>
          <ProductVisual slug={product.slug} title={product.title} variant={isBook ? "cover" : "object"} byline={isBook ? product.highlights[0]?.value : undefined} />
        </Link>
        <button className="product-card__save" type="button" aria-label={format(saved ? t.product.unsave : t.product.save, { title: product.title })} aria-pressed={saved} onClick={() => toggleSaved(product.id)}>
          <HeartIcon fill={saved ? "currentColor" : "none"} />
        </button>
      </div>

      <div className="product-card__body">
        <p className="product-card__brand t-meta" lang={CATALOG_LANG} dir="auto">{product.brand}</p>
        <h3 className="t-product" lang={CATALOG_LANG} dir="auto"><Link href={`/products/${product.slug}`} prefetch={false}>{product.title}</Link></h3>
        {product.highlights.length ? (
          <dl className="product-card__specs" lang={CATALOG_LANG} dir="auto">
            {product.highlights.map((spec) => <div key={spec.label}><dt className="sr-only">{spec.label}</dt><dd>{specText(spec)}</dd></div>)}
          </dl>
        ) : null}
        <p className="product-card__rating" aria-label={plural(t.product.rating, product.reviewCount, intl, { rating: product.rating.toFixed(1) })}>
          <StarIcon /><span>{product.rating.toFixed(1)}</span><span className="product-card__reviews">({product.reviewCount})</span>
        </p>
        <div className="product-card__buy">
          <p className="product-card__price">
            <strong>{formatMoney(product.price, intl)}</strong>
            {product.listPrice ? <s>{formatMoney(product.listPrice, intl)}</s> : null}
          </p>
          <Button variant="secondary" size="small" className="product-card__cta" busy={addState === "loading"} disabled={unavailable || !onAdd} onClick={addToCart}>{label}</Button>
        </div>
        {/* Out of stock is already said by the disabled button; only "limited" needs its own line. */}
        {product.availabilityHint === "low_stock" ? <p className="product-card__availability product-card__availability--low_stock">{t.availability.low_stock}</p> : null}
      </div>
    </article>
  );
}
