"use client";

import { Link, useIntlLocale, useT } from "@/components/providers/locale-provider";
import { useRequireSignIn } from "@/components/shell/sign-in-gate";
import { motion } from "motion/react";
import { useState } from "react";
import { CheckIcon, StarIcon } from "@/components/icons";
import { ProductGrid } from "@/components/product/product-grid";
import { Button } from "@/components/ui/button";
import { ProductVisual } from "@/components/ui/product-visual";
import { formatMoney } from "@/lib/api";
import { motionTokens } from "@/lib/motion";
import type { StorefrontProduct } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";

export function ProductDetailView({ data }: { data: StorefrontProduct }) {
  const t = useT();
  const intl = useIntlLocale();
  const { product, recommendations } = data;
  const requireSignIn = useRequireSignIn();
  const [variantId, setVariantId] = useState(product.variants.find((variant) => variant.availabilityHint !== "out_of_stock")?.id ?? product.variants[0]?.id ?? "");
  const [state, setState] = useState<"idle" | "loading" | "success" | "error">("idle");
  const add = useCartStore((store) => store.add);
  const open = useCartStore((store) => store.open);
  const selected = product.variants.find((variant) => variant.id === variantId);
  const unavailable = !selected || selected.availabilityHint === "out_of_stock";

  async function addSelected() {
    if (!selected || unavailable) return;
    if (!requireSignIn("bag")) return;
    setState("loading");
    try {
      await add(selected.id);
      setState("success");
      window.setTimeout(() => setState("idle"), 1800);
    } catch {
      setState("error");
    }
  }

  return (
    <main className="product-page">
      <nav className="breadcrumbs" aria-label="Breadcrumb"><Link href="/">Home</Link><span>/</span><Link href="/search">Goods</Link><span>/</span><span>{product.title}</span></nav>
      <section className="product-detail">
        <motion.div className="product-gallery" initial={false} animate={{ opacity: 1 }} transition={{ duration: motionTokens.duration.cinematic }}>
          <ProductVisual slug={product.slug} title={product.title} />
          <span className="product-gallery__index">01 / 01</span>
        </motion.div>
        <motion.div className="product-info" initial={false} animate={{ opacity: 1, y: 0 }} transition={{ duration: motionTokens.duration.deliberate, delay: 0.1, ease: motionTokens.easing.enter }}>
          <p className="eyebrow">{product.brand} · {product.category}</p>
          <h1>{product.title}</h1>
          <div className="product-info__rating"><StarIcon /><strong>{product.rating.toFixed(1)}</strong><span>{product.reviewCount} reviews</span></div>
          <div className="product-info__price"><strong>{selected ? formatMoney(selected.price, intl) : "Unavailable"}</strong>{selected?.listPrice ? <s>{formatMoney(selected.listPrice, intl)}</s> : null}</div>
          <p className="product-info__description">{product.description}</p>

          {product.variants.length > 1 ? <fieldset className="variant-picker"><legend>Choose an option</legend>{product.variants.map((variant) => <label key={variant.id}><input type="radio" name="variant" value={variant.id} checked={variantId === variant.id} onChange={() => setVariantId(variant.id)} disabled={variant.availabilityHint === "out_of_stock"} /><span>{variant.name}<small>{formatMoney(variant.price, intl)}</small></span></label>)}</fieldset> : null}
          <p className={`stock-line stock-line--${selected?.availabilityHint ?? "out_of_stock"}`}><span />{selected ? t.availability[selected.availabilityHint as keyof typeof t.availability] : t.availability.out_of_stock}</p>
          <Button size="large" className="product-info__add" busy={state === "loading"} disabled={unavailable} onClick={addSelected}>{state === "success" ? t.addToCart.success : state === "error" ? t.addToCart.error : t.addToCart.idle}</Button>
          {state === "success" ? <button className="text-button product-info__view-cart" onClick={() => void open()}>View your cart</button> : null}
          <ul className="product-promises"><li><CheckIcon /><span><strong>30-day returns</strong>Change your mind, no awkward questions.</span></li><li><CheckIcon /><span><strong>Thoughtful delivery</strong>Tracked and packed without excess.</span></li><li><CheckIcon /><span><strong>Real support</strong>Helpful humans, when you need one.</span></li></ul>
        </motion.div>
      </section>
      <section className="product-story"><p className="eyebrow">Why it earned a place</p><p>Dependable where it matters, considered where you notice. This is the kind of everyday object that quietly makes the routine better.</p></section>
      {recommendations.length ? <section className="product-section"><header className="section-heading"><div><p className="eyebrow">Pairs well with</p><h2>Keep exploring</h2></div></header><ProductGrid products={recommendations} /></section> : null}
    </main>
  );
}
