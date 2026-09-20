"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { ProductVisual } from "@/components/ui/product-visual";
import { ApiError, browserRequest, formatMoney } from "@/lib/api";
import type { CheckoutResult, ShippingAddress } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";

export function CheckoutForm() {
  const router = useRouter();
  const { cart, status, error: cartError, isLoading, load } = useCartStore();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const checkoutAttempt = useRef<{ fingerprint: string; key: string } | null>(null);

  async function keyFor(payload: string) {
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(payload));
    const fingerprint = Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
    if (checkoutAttempt.current?.fingerprint === fingerprint) return checkoutAttempt.current.key;
    const storageKey = "amaron:checkout-attempt";
    try {
      const stored = JSON.parse(sessionStorage.getItem(storageKey) ?? "null") as { fingerprint?: string; key?: string } | null;
      if (stored?.fingerprint === fingerprint && stored.key) checkoutAttempt.current = { fingerprint, key: stored.key };
      else {
        checkoutAttempt.current = { fingerprint, key: crypto.randomUUID() };
        sessionStorage.setItem(storageKey, JSON.stringify(checkoutAttempt.current));
      }
    } catch { checkoutAttempt.current = { fingerprint, key: crypto.randomUUID() }; }
    return checkoutAttempt.current.key;
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    const values = new FormData(event.currentTarget);
    const shippingAddress: ShippingAddress = {
      recipient: String(values.get("recipient") ?? ""), line1: String(values.get("line1") ?? ""), line2: String(values.get("line2") ?? ""),
      city: String(values.get("city") ?? ""), region: String(values.get("region") ?? ""), postalCode: String(values.get("postalCode") ?? ""), countryCode: String(values.get("countryCode") ?? "US"),
    };
    const payload = JSON.stringify({ cartId: cart?.cartId, cartVersion: cart?.version, shippingAddress });
    try {
      const result = await browserRequest<CheckoutResult>("/api/checkout/confirm", { method: "POST", headers: { "Idempotency-Key": await keyFor(payload) }, body: JSON.stringify({ shippingAddress }) }, 22_000);
      try { sessionStorage.removeItem("amaron:checkout-attempt"); } catch { /* Storage is an optimization, not a checkout dependency. */ }
      checkoutAttempt.current = null;
      await load();
      router.push(`/orders/${result.order.id}?placed=1`);
    } catch (caught) {
      setError(caught instanceof ApiError && caught.status < 500 ? caught.message : "We couldn't confirm whether the order was placed. Retrying with the same details is safe.");
    } finally {
      setSubmitting(false);
    }
  }

  if ((status === "idle" || status === "loading") && !cart) return <main className="checkout-page"><p role="status">Preparing checkout…</p></main>;
  if (status === "error" && !cart) return <main className="checkout-page checkout-empty"><p className="eyebrow">Cart unavailable</p><h1>We couldn&apos;t load your cart.</h1><p>{cartError ?? "Try again in a moment."}</p><Button onClick={() => void load()} busy={isLoading}>Try again</Button></main>;
  if (status === "ready" && !cart?.items.length) return <main className="checkout-page checkout-empty"><p className="eyebrow">Nothing to check out</p><h1>Your cart is feeling a little empty</h1><p>Browse around and add something you like.</p><Link className="button button--primary button--large" href="/search">Start shopping</Link></main>;
  if (!cart) return null;

  return (
    <main className="checkout-page">
      <header className="checkout-heading"><p className="eyebrow">Secure checkout</p><h1>Where should we send it?</h1><p>No account maze. Just the details we need to deliver your order.</p></header>
      <div className="checkout-layout">
        <form className="checkout-form" onSubmit={submit}>
          <fieldset><legend>Contact & delivery</legend>
            <label className="field field--wide"><span>Full name</span><input name="recipient" autoComplete="name" required maxLength={120} /></label>
            <label className="field field--wide"><span>Street address</span><input name="line1" autoComplete="address-line1" required maxLength={200} /></label>
            <label className="field field--wide"><span>Apartment, suite, etc. <small>Optional</small></span><input name="line2" autoComplete="address-line2" maxLength={200} /></label>
            <label className="field"><span>City</span><input name="city" autoComplete="address-level2" required maxLength={100} /></label>
            <label className="field"><span>State / region</span><input name="region" autoComplete="address-level1" required maxLength={100} /></label>
            <label className="field"><span>Postal code</span><input name="postalCode" autoComplete="postal-code" required maxLength={24} /></label>
            <label className="field"><span>Country</span><select name="countryCode" autoComplete="country" defaultValue="US"><option value="US">United States</option><option value="CA">Canada</option><option value="GB">United Kingdom</option><option value="AU">Australia</option></select></label>
          </fieldset>
          {error ? <div className="checkout-error" role="alert"><strong>We couldn&apos;t confirm the order.</strong><span>{error}</span></div> : null}
          <Button size="large" busy={submitting} type="submit">{submitting ? "Placing your order…" : `Place order · ${formatMoney(cart.subtotal)}`}</Button>
          <p className="checkout-assurance">Inventory and pricing are confirmed once more before the order is placed.</p>
        </form>
        <aside className="order-summary"><p className="eyebrow">Your order</p><h2>{cart.totalQuantity} {cart.totalQuantity === 1 ? "item" : "items"}</h2><ul>{cart.items.map((item) => <li key={item.variantId}><div><ProductVisual compact slug={item.slug} title={item.title} /><b>{item.quantity}</b></div><span><strong>{item.title}</strong><small>{item.variant}</small></span><strong>{formatMoney(item.lineTotal)}</strong></li>)}</ul><div className="order-summary__total"><span>Subtotal</span><strong>{formatMoney(cart.subtotal)}</strong></div><p>Delivery and taxes are included in the final confirmation.</p></aside>
      </div>
    </main>
  );
}
