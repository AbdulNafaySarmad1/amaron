"use client";

import { useEffect, useState } from "react";
import { CheckIcon, HeartIcon, StarIcon } from "@/components/icons";
import { ProductGrid } from "@/components/product/product-grid";
import { Link, useIntlLocale, useT } from "@/components/providers/locale-provider";
import { useRequireSignIn } from "@/components/shell/sign-in-gate";
import { Button } from "@/components/ui/button";
import { ProductVisual } from "@/components/ui/product-visual";
import { CATALOG_LANG } from "@/i18n/config";
import { format, plural } from "@/i18n/dictionary";
import { formatMoney } from "@/lib/api";
import { recordViewed } from "@/lib/recently-viewed";
import { splitDescription } from "@/lib/specs";
import type { StorefrontProduct } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";
import { useSavedStore } from "@/store/saved-store";

/** Calm first viewport (what it is, what it costs, can I have it), then detail on demand below. */
export function ProductDetailView({ data }: { data: StorefrontProduct }) {
  const t = useT();
  const intl = useIntlLocale();
  const { product, recommendations } = data;
  const requireSignIn = useRequireSignIn();
  const [variantId, setVariantId] = useState(product.variants.find((variant) => variant.availabilityHint !== "out_of_stock")?.id ?? product.variants[0]?.id ?? "");
  const [state, setState] = useState<"idle" | "loading" | "success" | "error">("idle");
  const add = useCartStore((store) => store.add);
  const openBag = useCartStore((store) => store.open);
  const saved = useSavedStore((store) => store.ids.includes(product.id));
  const toggleSaved = useSavedStore((store) => store.toggle);
  const selected = product.variants.find((variant) => variant.id === variantId);
  const unavailable = !selected || selected.availabilityHint === "out_of_stock";
  const isBook = product.kind === "book";
  const { lead, more } = splitDescription(product.description);
  useEffect(() => { recordViewed(product.id); }, [product.id]);
  const saving = selected?.listPrice && selected.listPrice.amount > selected.price.amount ? { amount: selected.listPrice.amount - selected.price.amount, currency: selected.price.currency } : null;

  async function addSelected() {
    if (!selected || unavailable) return;
    if (!requireSignIn("bag")) return;
    setState("loading");
    try {
      await add(selected.id);
      setState("success");
      window.setTimeout(() => setState("idle"), 2400);
    } catch {
      setState("error");
    }
  }

  return (
    <main className="page product-page">
      <nav className="breadcrumbs" aria-label={t.category.breadcrumb}>
        <ol>
          <li><Link href="/">{t.category.home}</Link></li>
          <li><Link href={`/c/${product.categorySlug}`} lang={CATALOG_LANG} dir="auto">{product.category}</Link></li>
          <li><span aria-current="page" lang={CATALOG_LANG} dir="auto">{product.title}</span></li>
        </ol>
      </nav>

      <section className={`pdp pdp--${isBook ? "book" : "object"}`}>
        <div className="pdp__gallery">
          <ProductVisual slug={product.slug} title={product.title} variant={isBook ? "cover" : "object"} byline={isBook ? product.specifications.find((spec) => spec.label === "Author")?.value : undefined} />
        </div>

        <div className="pdp__buy">
          <p className="t-caption" lang={CATALOG_LANG} dir="auto">{product.brand}</p>
          <h1 className="t-h1" lang={CATALOG_LANG} dir="auto">{product.title}</h1>
          {lead ? <p className="pdp__lead" lang={CATALOG_LANG} dir="auto">{lead}</p> : null}
          {product.reviewCount ? (
            <p className="pdp__rating" aria-label={plural(t.pdp.ratingLabel, product.reviewCount, intl, { rating: product.rating.toFixed(1) })}>
              <StarIcon /><strong>{product.rating.toFixed(1)}</strong><span>{plural(t.pdp.reviews, product.reviewCount, intl)}</span>
            </p>
          ) : null}

          <p className="pdp__price">
            <strong>{selected ? formatMoney(selected.price, intl) : t.pdp.unavailable}</strong>
            {selected?.listPrice && saving ? <><s>{formatMoney(selected.listPrice, intl)}</s><span className="pdp__saving">{format(t.pdp.youSave, { amount: formatMoney(saving, intl) })}</span></> : null}
          </p>

          {product.variants.length > 1 ? (
            <fieldset className="variant-picker">
              <legend>{t.pdp.option}</legend>
              {product.variants.map((variant) => (
                <label key={variant.id}>
                  <input type="radio" name="variant" value={variant.id} checked={variantId === variant.id} onChange={() => setVariantId(variant.id)} disabled={variant.availabilityHint === "out_of_stock"} />
                  <span><span lang={CATALOG_LANG} dir="auto">{variant.name}</span><small>{formatMoney(variant.price, intl)}</small></span>
                </label>
              ))}
            </fieldset>
          ) : null}

          <p className={`stock-line stock-line--${selected?.availabilityHint ?? "out_of_stock"}`}><span />{selected ? t.availability[selected.availabilityHint as keyof typeof t.availability] : t.availability.out_of_stock}</p>

          <div className="pdp__actions">
            <Button size="large" className="pdp__add" busy={state === "loading"} disabled={unavailable} onClick={addSelected}>
              {state === "success" ? t.addToCart.success : state === "error" ? t.addToCart.error : unavailable ? t.addToCart.unavailable : t.addToCart.idle}
            </Button>
            <Button variant="secondary" size="large" className="pdp__save" aria-pressed={saved} onClick={() => toggleSaved(product.id)} icon={<HeartIcon fill={saved ? "currentColor" : "none"} />}>
              {saved ? t.pdp.saved : t.pdp.save}
            </Button>
          </div>
          <p className="pdp__after-add" role="status">
            {state === "success" ? <button type="button" className="text-button" onClick={() => void openBag()}>{t.pdp.viewBag}</button> : null}
          </p>

          <ul className="pdp__promises">
            <li><CheckIcon /><span><strong>{t.pdp.deliveryTitle}</strong>{t.pdp.deliveryBody}</span></li>
            <li><CheckIcon /><span><strong>{t.pdp.returnsTitle}</strong>{t.pdp.returnsBody}</span></li>
            <li><CheckIcon /><span><strong>{t.pdp.supportTitle}</strong>{t.pdp.supportBody}</span></li>
          </ul>
        </div>
      </section>

      <div className="pdp__sections">
        {more ? (
          <section aria-labelledby="pdp-details">
            <h2 id="pdp-details" className="t-h2">{t.pdp.details}</h2>
            <p className="t-body" lang={CATALOG_LANG} dir="auto">{more}</p>
          </section>
        ) : null}
        {product.specifications.length ? (
          <section aria-labelledby="pdp-specs">
            <h2 id="pdp-specs" className="t-h2">{t.pdp.specifications}</h2>
            <dl className="spec-table" lang={CATALOG_LANG} dir="auto">
              {product.specifications.map((spec) => <div key={spec.label}><dt>{spec.label}</dt><dd>{spec.value}</dd></div>)}
            </dl>
          </section>
        ) : null}
        <section aria-labelledby="pdp-delivery">
          <h2 id="pdp-delivery" className="t-h2">{t.pdp.deliveryReturns}</h2>
          <p className="t-body">{t.pdp.deliveryReturnsBody}</p>
        </section>
      </div>

      {recommendations.length ? (
        <section className="pdp__more" aria-labelledby="pdp-more">
          <h2 id="pdp-more" className="t-h2">{format(t.pdp.moreIn, { category: product.category })}</h2>
          <ProductGrid products={recommendations.slice(0, 4)} />
        </section>
      ) : null}
    </main>
  );
}
