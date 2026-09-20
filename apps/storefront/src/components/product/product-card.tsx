"use client";

import Link from "next/link";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { useState } from "react";
import { HeartIcon, StarIcon } from "@/components/icons";
import { Button } from "@/components/ui/button";
import { ProductVisual } from "@/components/ui/product-visual";
import { commerceCopy } from "@/content/commerce";
import { formatMoney } from "@/lib/api";
import { motionTokens } from "@/lib/motion";
import type { ProductCardModel } from "@/lib/types";

type AddState = "idle" | "loading" | "success" | "error";

export function ProductCard({ product, onAdd, priority = false }: { product: ProductCardModel; onAdd?: (variantId: string) => Promise<void>; priority?: boolean }) {
  const [addState, setAddState] = useState<AddState>("idle");
  const [saved, setSaved] = useState(false);
  const reducedMotion = useReducedMotion();
  const unavailable = product.availabilityHint === "out_of_stock";

  async function addToCart() {
    if (!onAdd || unavailable || addState === "loading") return;
    setAddState("loading");
    try {
      await onAdd(product.defaultVariantId);
      setAddState("success");
      window.setTimeout(() => setAddState("idle"), 1400);
    } catch {
      setAddState("error");
      window.setTimeout(() => setAddState("idle"), 2200);
    }
  }

  const label = unavailable
    ? commerceCopy.addToCart.unavailable
    : addState === "loading"
      ? commerceCopy.addToCart.loading
      : addState === "success"
        ? commerceCopy.addToCart.success
        : addState === "error" ? "Try again" : commerceCopy.addToCart.idle;

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
        <Link href={`/products/${product.slug}`} prefetch={false} className="product-card__image-link" aria-label={`View ${product.title}`}>
          <motion.div className="product-card__visual" whileHover={reducedMotion ? undefined : { scale: 1.035 }} transition={{ duration: motionTokens.duration.deliberate, ease: motionTokens.easing.standard }}>
            <ProductVisual slug={product.slug} title={product.title} />
          </motion.div>
        </Link>
        <button className="product-card__save" type="button" aria-label={saved ? `Remove ${product.title} from saved items` : `Save ${product.title} for later`} aria-pressed={saved} onClick={() => setSaved((value) => !value)}>
          <HeartIcon fill={saved ? "currentColor" : "none"} />
        </button>
        {product.badges.includes("featured") ? <span className="product-card__badge">Editor&apos;s pick</span> : null}
      </div>

      <div className="product-card__body">
        <p className="product-card__brand">{product.brand}</p>
        <h3><Link href={`/products/${product.slug}`} prefetch={false}>{product.title}</Link></h3>
        <div className="product-card__rating" aria-label={`${product.rating} out of 5 stars from ${product.reviewCount} reviews`}>
          <StarIcon /><span>{product.rating.toFixed(1)}</span><span className="product-card__reviews">({product.reviewCount})</span>
        </div>
        <div className="product-card__price-row">
          <strong>{formatMoney(product.price)}</strong>
          {product.listPrice ? <s>{formatMoney(product.listPrice)}</s> : null}
        </div>
        <p className={`product-card__availability product-card__availability--${product.availabilityHint}`}>{commerceCopy.availability[product.availabilityHint]}</p>
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
