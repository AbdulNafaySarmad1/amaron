"use client";

import Link from "next/link";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { useEffect, useRef } from "react";
import { CloseIcon, MinusIcon, PlusIcon } from "@/components/icons";
import { ProductVisual } from "@/components/ui/product-visual";
import { commerceCopy } from "@/content/commerce";
import { formatMoney } from "@/lib/api";
import { motionTokens } from "@/lib/motion";
import { useCartStore } from "@/store/cart-store";

export function CartDrawer() {
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
          <motion.button type="button" className="cart-backdrop" aria-label="Close cart" onClick={close} initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} />
          <motion.aside
            ref={drawer}
            className="cart-drawer"
            role="dialog"
            aria-modal="true"
            aria-labelledby="cart-title"
            initial={reducedMotion ? { opacity: 0 } : { x: "100%" }}
            animate={reducedMotion ? { opacity: 1 } : { x: 0 }}
            exit={reducedMotion ? { opacity: 0 } : { x: "100%" }}
            transition={motionTokens.spring.drawer}
          >
            <header className="cart-drawer__header">
              <div><p className="eyebrow">{cart?.totalQuantity ?? 0} items</p><h2 id="cart-title">{commerceCopy.cart.title}</h2></div>
              <button ref={closeButton} className="icon-button" onClick={close} aria-label="Close cart"><CloseIcon /></button>
            </header>
            <div className="cart-drawer__body" aria-busy={isLoading}>
              {status === "error" && !cart ? <div className="cart-load-error" role="alert"><p>{error ?? "The cart could not be loaded."}</p><button className="button button--secondary button--small" onClick={() => void load()}>Try again</button></div> : null}
              {status === "loading" && !cart ? <p role="status">Loading your cart…</p> : null}
              {error && cart ? <p className="inline-error" role="alert">{error}</p> : null}
              {status === "ready" && !cart?.items.length ? (
                <div className="cart-empty">
                  <span className="cart-empty__mark">0</span>
                  <h3>{commerceCopy.cart.emptyTitle}</h3>
                  <p>{commerceCopy.cart.emptyBody}</p>
                  <Link className="button button--primary button--medium" href="/search" onClick={close}>{commerceCopy.cart.startShopping}</Link>
                </div>
              ) : cart?.items.length ? (
                <ul className="cart-lines">
                  {cart.items.map((item) => (
                    <li key={item.variantId} className="cart-line">
                      <Link className="cart-line__visual" href={`/products/${item.slug}`} onClick={close}><ProductVisual compact slug={item.slug} title={item.title} /></Link>
                      <div className="cart-line__details">
                        <Link href={`/products/${item.slug}`} onClick={close}><strong>{item.title}</strong></Link>
                        <span>{item.variant}</span>
                        <strong>{formatMoney(item.lineTotal)}</strong>
                        <div className="cart-line__actions">
                          <div className="quantity-stepper" aria-label={`Quantity for ${item.title}`}>
                            <button aria-label="Decrease quantity" disabled={isLoading} onClick={() => void setQuantity(item.variantId, item.quantity - 1)}><MinusIcon /></button>
                            <span aria-live="polite">{item.quantity}</span>
                            <button aria-label="Increase quantity" disabled={isLoading || item.quantity >= 99} onClick={() => void setQuantity(item.variantId, item.quantity + 1)}><PlusIcon /></button>
                          </div>
                          <button className="text-button" disabled={isLoading} onClick={() => void remove(item.variantId)}>Remove</button>
                        </div>
                      </div>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
            {cart?.items.length ? (
              <footer className="cart-drawer__footer">
                <div><span>Subtotal</span><strong>{formatMoney(cart.subtotal)}</strong></div>
                <p>Taxes and delivery are calculated at checkout.</p>
                <Link className="button button--primary button--large" href="/checkout" onClick={close}>Continue to checkout</Link>
              </footer>
            ) : null}
          </motion.aside>
        </div>
      ) : null}
    </AnimatePresence>
  );
}
