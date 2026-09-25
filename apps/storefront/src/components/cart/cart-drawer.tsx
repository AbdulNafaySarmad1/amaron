"use client";

import { Link, useIntlLocale, useLocale, useT } from "@/components/providers/locale-provider";
import { CATALOG_LANG, localeDirection } from "@/i18n/config";
import { format, plural } from "@/i18n/dictionary";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { useEffect, useRef } from "react";
import { CloseIcon, MinusIcon, PlusIcon } from "@/components/icons";
import { ProductVisual } from "@/components/ui/product-visual";
import { formatMoney } from "@/lib/api";
import { motionTokens } from "@/lib/motion";
import { useCartStore } from "@/store/cart-store";

export function CartDrawer() {
  const t = useT();
  const intl = useIntlLocale();
  // The drawer sits on the inline-end edge, so in right-to-left languages it enters from the left.
  const offscreen = localeDirection(useLocale()) === "rtl" ? "-100%" : "100%";
  const { cart, status, isOpen, isLoading, error, load, close, setQuantity, remove } = useCartStore();
  const reducedMotion = useReducedMotion();
  const closeButton = useRef<HTMLButtonElement>(null);
  const drawer = useRef<HTMLElement>(null);

  useEffect(() => {
    if (!isOpen) return;
    const returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    closeButton.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") close();
      if (event.key !== "Tab" || !drawer.current) return;
      const focusable = Array.from(drawer.current.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'));
      const first = focusable[0];
      const last = focusable.at(-1);
      if (!first || !last) return;
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => { document.body.style.overflow = previousOverflow; window.removeEventListener("keydown", onKeyDown); returnFocus?.focus(); };
  }, [close, isOpen]);

  return (
    <AnimatePresence>
      {isOpen ? (
        <div className="cart-layer">
          <motion.button type="button" className="cart-backdrop" aria-label={t.cart.close} onClick={close} initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} />
          <motion.aside
            ref={drawer}
            className="cart-drawer"
            role="dialog"
            aria-modal="true"
            aria-labelledby="cart-title"
            initial={reducedMotion ? { opacity: 0 } : { x: offscreen }}
            animate={reducedMotion ? { opacity: 1 } : { x: 0 }}
            exit={reducedMotion ? { opacity: 0 } : { x: offscreen }}
            transition={motionTokens.spring.drawer}
          >
            <header className="cart-drawer__header">
              <div><p className="eyebrow">{plural(t.cart.items, cart?.totalQuantity ?? 0, intl)}</p><h2 id="cart-title">{t.cart.title}</h2></div>
              <button ref={closeButton} className="icon-button" onClick={close} aria-label={t.cart.close}><CloseIcon /></button>
            </header>
            <div className="cart-drawer__body" aria-busy={isLoading}>
              {status === "error" && !cart ? <div className="cart-load-error" role="alert"><p>{error ?? t.cart.loadError}</p><button className="button button--secondary button--small" onClick={() => void load()}>{t.cart.retry}</button></div> : null}
              {status === "loading" && !cart ? <p role="status">{t.cart.loading}</p> : null}
              {error && cart ? <p className="inline-error" role="alert">{error}</p> : null}
              {status === "ready" && !cart?.items.length ? (
                <div className="cart-empty">
                  <span className="cart-empty__mark">0</span>
                  <h3>{t.cart.emptyTitle}</h3>
                  <p>{t.cart.emptyBody}</p>
                  <Link className="button button--primary button--medium" href="/search" onClick={close}>{t.cart.startShopping}</Link>
                </div>
              ) : cart?.items.length ? (
                <ul className="cart-lines">
                  {cart.items.map((item) => (
                    <li key={item.variantId} className="cart-line">
                      <Link className="cart-line__visual" href={`/products/${item.slug}`} onClick={close}><ProductVisual compact slug={item.slug} title={item.title} /></Link>
                      <div className="cart-line__details">
                        <Link href={`/products/${item.slug}`} onClick={close}><strong lang={CATALOG_LANG} dir="auto">{item.title}</strong></Link>
                        <span lang={CATALOG_LANG} dir="auto">{item.variant}</span>
                        <strong>{formatMoney(item.lineTotal, intl)}</strong>
                        <div className="cart-line__actions">
                          <div className="quantity-stepper" aria-label={format(t.cart.quantityFor, { title: item.title })}>
                            <button aria-label={t.cart.decrease} disabled={isLoading} onClick={() => void setQuantity(item.variantId, item.quantity - 1)}><MinusIcon /></button>
                            <span aria-live="polite">{item.quantity}</span>
                            <button aria-label={t.cart.increase} disabled={isLoading || item.quantity >= 99} onClick={() => void setQuantity(item.variantId, item.quantity + 1)}><PlusIcon /></button>
                          </div>
                          <button className="text-button" disabled={isLoading} onClick={() => void remove(item.variantId)}>{t.cart.remove}</button>
                        </div>
                      </div>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
            {cart?.items.length ? (
              <footer className="cart-drawer__footer">
                <div><span>{t.cart.subtotal}</span><strong>{formatMoney(cart.subtotal, intl)}</strong></div>
                <p>{t.cart.taxesNote}</p>
                <Link className="button button--primary button--large" href="/checkout" onClick={close}>{t.cart.checkout}</Link>
              </footer>
            ) : null}
          </motion.aside>
        </div>
      ) : null}
    </AnimatePresence>
  );
}
