"use client";

import { Link, useIntlLocale, useT } from "@/components/providers/locale-provider";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { useState } from "react";
import { HeartIcon, StarIcon } from "@/components/icons";
import { Button } from "@/components/ui/button";
import { ProductVisual } from "@/components/ui/product-visual";
import { CATALOG_LANG } from "@/i18n/config";
import { format } from "@/i18n/dictionary";
import { formatMoney } from "@/lib/api";
import { motionTokens } from "@/lib/motion";
import { useSavedStore } from "@/store/saved-store";
import type { ProductCardModel } from "@/lib/types";

type AddState = "idle" | "loading" | "success" | "error";

export function ProductCard({ product, onAdd, priority = false }: { product: ProductCardModel; onAdd?: (variantId: string) => Promise<boolean | void>; priority?: boolean }) {
  const t = useT();
  const intl = useIntlLocale();
  const [addState, setAddState] = useState<AddState>("idle");
  const saved = useSavedStore((state) => state.ids.includes(product.id));
  const toggleSaved = useSavedStore((state) => state.toggle);
  const reducedMotion = useReducedMotion();
  const unavailable = product.availabilityHint === "out_of_stock";

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

  const label = unavailable
    ? t.addToCart.unavailable
    : addState === "loading"
      ? t.addToCart.loading
      : addState === "success"
        ? t.addToCart.success
        : addState === "error" ? t.addToCart.error : t.addToCart.idle;

  return (
    <motion.article
      layout
      className="product-card"
      initial={false}
      animate={{ opacity: 1, y: 0 }}
      exit={reducedMotion ? { opacity: 0 } : { opacity: 0, y: -8 }}
      transition={motionTokens.spring.layout}
      data-priority={priority || undefined}
    >
      <div className="product-card__media">
        <Link href={`/products/${product.slug}`} prefetch={false} className="product-card__image-link" aria-label={format(t.product.view, { title: product.title })}>
          <motion.div className="product-card__visual" whileHover={reducedMotion ? undefined : { scale: 1.035 }} transition={{ duration: motionTokens.duration.deliberate, ease: motionTokens.easing.standard }}>
            <ProductVisual slug={product.slug} title={product.title} />
          </motion.div>
        </Link>
        <button className="product-card__save" type="button" aria-label={format(saved ? t.product.unsave : t.product.save, { title: product.title })} aria-pressed={saved} onClick={() => toggleSaved(product.id)}>
          <HeartIcon fill={saved ? "currentColor" : "none"} />
        </button>
        {product.badges.includes("featured") ? <span className="product-card__badge">{t.product.editorsPick}</span> : null}
      </div>

      <div className="product-card__body">
        <p className="product-card__brand" lang={CATALOG_LANG} dir="auto">{product.brand}</p>
        <h3 lang={CATALOG_LANG} dir="auto"><Link href={`/products/${product.slug}`} prefetch={false}>{product.title}</Link></h3>
        <div className="product-card__rating" aria-label={format(t.product.rating, { rating: product.rating, count: product.reviewCount })}>
          <StarIcon /><span>{product.rating.toFixed(1)}</span><span className="product-card__reviews">({product.reviewCount})</span>
        </div>
        <div className="product-card__price-row">
          <strong>{formatMoney(product.price, intl)}</strong>
          {product.listPrice ? <s>{formatMoney(product.listPrice, intl)}</s> : null}
        </div>
        <p className={`product-card__availability product-card__availability--${product.availabilityHint}`}>{t.availability[product.availabilityHint]}</p>
        <Button className="product-card__cta" busy={addState === "loading"} disabled={unavailable || !onAdd} onClick={addToCart}>
          <AnimatePresence mode="popLayout" initial={false}>
            <motion.span key={label} initial={reducedMotion ? false : { opacity: 0, y: 5 }} animate={{ opacity: 1, y: 0 }} exit={reducedMotion ? { opacity: 0 } : { opacity: 0, y: -5 }} transition={{ duration: motionTokens.duration.quick }}>
              {label}
            </motion.span>
          </AnimatePresence>
        </Button>
      </div>
    </motion.article>
  );
}
