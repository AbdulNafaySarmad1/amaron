"use client";

import Script from "next/script";
import { FormEvent, type RefObject, useEffect, useRef, useState } from "react";
import { Link, useIntlLocale, useLocaleContext, useLocalizedRouter } from "@/components/providers/locale-provider";
import { Button } from "@/components/ui/button";
import { ProductVisual } from "@/components/ui/product-visual";
import { CATALOG_LANG, regions } from "@/i18n/config";
import { format, isolate } from "@/i18n/dictionary";
import { ApiError, browserRequest, formatMoney } from "@/lib/api";
import type { CheckoutResult, Payment, ShippingAddress } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";
import { itemFrom, track } from "@/lib/telemetry";
import type { Cart } from "@/lib/types";

const cartItems = (cart: Cart) => cart.items.map((i) => itemFrom({ id: i.productId, title: i.title, variant: i.variant, price: i.unitPrice, quantity: i.quantity }));

type TurnstileApi = { render(el: HTMLElement, options: Record<string, unknown>): string; reset(id: string): void; remove(id: string): void };
const turnstileApi = () => (window as Window & { turnstile?: TurnstileApi }).turnstile;
const TURNSTILE_SITE_KEY = process.env.NEXT_PUBLIC_TURNSTILE_SITE_KEY;

/** Cloudflare Turnstile, rendered only when a site key is configured. Invisible unless Cloudflare wants an interaction. */
function Turnstile({ siteKey, widgetRef }: { siteKey: string; widgetRef: RefObject<string | null> }) {
  const container = useRef<HTMLDivElement>(null);
  const [ready, setReady] = useState(false);
  useEffect(() => {
    const api = turnstileApi();
    if (!ready || !api || !container.current) return;
    const id = api.render(container.current, { sitekey: siteKey, appearance: "interaction-only", "response-field-name": "turnstileToken" });
    widgetRef.current = id;
    return () => { api.remove(id); widgetRef.current = null; };
  }, [ready, siteKey, widgetRef]);
  return <><Script src="https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit" onReady={() => setReady(true)} /><div ref={container} className="checkout-challenge" /></>;
}

const METHODS = ["Card", "Raast", "BankTransfer", "CashOnDelivery", "Installment"] as const;

/** Checkout stops selling: address, payment, review. Nothing else competes for attention. */
export function CheckoutForm() {
  const router = useLocalizedRouter();
  const { dictionary, region } = useLocaleContext();
  const t = dictionary.checkout;
  const intl = useIntlLocale();
  const { cart, status, error: cartError, isLoading, load } = useCartStore();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pendingPayment, setPendingPayment] = useState<Payment | null>(null);
  const checkoutAttempt = useRef<{ fingerprint: string; key: string } | null>(null);
  const turnstileWidget = useRef<string | null>(null);
  const cartId = cart?.cartId;
  useEffect(() => {
    if (cart?.items.length) track({ name: "begin_checkout", currency: cart.subtotal.currency, value: cart.subtotal.amount, items: cartItems(cart) });
    // Once per checkout visit.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cartId]);
  const countryName = (code: string) => new Intl.DisplayNames([intl], { type: "region" }).of(code) ?? code;

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
      city: String(values.get("city") ?? ""), region: String(values.get("region") ?? ""), postalCode: String(values.get("postalCode") ?? ""), countryCode: String(values.get("countryCode") ?? region.country),
    };
    const paymentMethod = String(values.get("paymentMethod") ?? "Card");
    const token = String(values.get("paymentToken") ?? "");
    const challenge = String(values.get("turnstileToken") ?? "");
    if (cart) {
      const priced = { currency: cart.subtotal.currency, value: cart.subtotal.amount, items: cartItems(cart) };
      track({ name: "add_shipping_info", ...priced, shipping_tier: shippingAddress.countryCode });
      track({ name: "add_payment_info", ...priced, payment_type: paymentMethod });
    }
    const payload = JSON.stringify({ cartId: cart?.cartId, cartVersion: cart?.version, shippingAddress, paymentMethod, token });
    try {
      const result = await browserRequest<CheckoutResult>("/api/bff/checkout/confirm", { method: "POST", headers: { "Idempotency-Key": await keyFor(payload), ...(challenge && { "X-Turnstile-Token": challenge }) }, body: JSON.stringify({ shippingAddress, paymentMethod: { method: paymentMethod, provider: "test", paymentMethodToken: token || undefined } }) }, 22_000);
      if (result.payment.status === "RequiresCustomerAction") { setPendingPayment(result.payment); return; }
      try { sessionStorage.removeItem("amaron:checkout-attempt"); } catch { /* Storage is an optimization, not a checkout dependency. */ }
      checkoutAttempt.current = null;
      await load();
      router.push(`/orders/${result.order.id}?placed=1`);
    } catch (caught) {
      setError(caught instanceof ApiError && caught.problem.code === "challenge_required" ? t.challengeFailed : caught instanceof ApiError && caught.status < 500 ? caught.message : t.errorUnknown);
    } finally {
      setSubmitting(false);
      // Tokens are single-use; get a fresh one for any retry.
      if (turnstileWidget.current) turnstileApi()?.reset(turnstileWidget.current);
    }
  }

  async function completeAuthentication() {
    if (!pendingPayment) return;
    setSubmitting(true); setError(null);
    try {
      const payment = await browserRequest<Payment>(`/api/bff/payments/${pendingPayment.id}/confirm`, { method: "POST", headers: { "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ paymentMethodToken: "test:complete" }) }, 22_000);
      setPendingPayment(null);
      router.push(`/orders/${payment.orderId}?payment=confirmed`);
    } catch { setError(t.errorPayment); }
    finally { setSubmitting(false); }
  }

  if ((status === "idle" || status === "loading") && !cart) return <main className="page checkout-page"><p role="status" className="t-meta">{t.preparing}</p></main>;
  if (status === "error" && !cart) return <main className="page checkout-page"><div className="empty-state" role="alert"><h1 className="t-h2">{t.loadErrorTitle}</h1><p>{cartError ?? t.loadErrorBody}</p><Button onClick={() => void load()} busy={isLoading}>{t.retry}</Button></div></main>;
  if (status === "ready" && !cart?.items.length) return <main className="page checkout-page"><div className="empty-state"><h1 className="t-h2">{t.emptyTitle}</h1><p>{t.emptyBody}</p><Link className="button button--primary button--medium" href="/search">{dictionary.cart.startShopping}</Link></div></main>;
  if (!cart) return null;

  const methodLabel: Record<(typeof METHODS)[number], string> = { Card: t.methodCard, Raast: t.methodRaast, BankTransfer: t.methodBankTransfer, CashOnDelivery: t.methodCashOnDelivery, Installment: t.methodInstallment };
  const total = formatMoney(cart.subtotal, intl);

  return (
    <main className="page checkout-page">
      <h1 className="t-h1">{t.title}</h1>
      <div className="checkout-layout">
        <form className="checkout-form" onSubmit={submit}>
          <section className="checkout-step" aria-labelledby="step-delivery">
            <h2 id="step-delivery" className="t-h3"><span aria-hidden="true">1</span>{t.delivery}</h2>
            <div className="checkout-fields">
              <label className="field field--wide"><span>{t.name}</span><input name="recipient" autoComplete="name" required maxLength={120} /></label>
              <label className="field field--wide"><span>{t.line1}</span><input name="line1" autoComplete="address-line1" required maxLength={200} /></label>
              <label className="field field--wide"><span>{t.line2} <small>{t.optional}</small></span><input name="line2" autoComplete="address-line2" maxLength={200} /></label>
              <label className="field"><span>{t.city}</span><input name="city" autoComplete="address-level2" required maxLength={100} /></label>
              <label className="field"><span>{t.region}</span><input name="region" autoComplete="address-level1" required maxLength={100} /></label>
              <label className="field"><span>{t.postalCode}</span><input name="postalCode" autoComplete="postal-code" required maxLength={24} /></label>
              <label className="field"><span>{t.country}</span>
                <select name="countryCode" autoComplete="country" defaultValue={region.country}>
                  {Object.keys(regions).sort((a, b) => countryName(a).localeCompare(countryName(b), intl)).map((code) => <option key={code} value={code}>{countryName(code)}</option>)}
                </select>
              </label>
            </div>
          </section>

          <section className="checkout-step" aria-labelledby="step-payment">
            <h2 id="step-payment" className="t-h3"><span aria-hidden="true">2</span>{t.payment}</h2>
            <fieldset className="payment-methods">
              <legend className="sr-only">{t.method}</legend>
              {METHODS.map((method, index) => (
                <label key={method} className="payment-method"><input type="radio" name="paymentMethod" value={method} defaultChecked={index === 0} /><span>{methodLabel[method]}</span></label>
              ))}
            </fieldset>
            <label className="field field--wide checkout-test"><span>{t.testOutcome} <small>{t.testOutcomeHint}</small></span>
              <select name="paymentToken" defaultValue="">
                <option value="">{t.outcomeApprove}</option>
                <option value="test:requires-action">{t.outcomeVerify}</option>
                <option value="test:decline">{t.outcomeDecline}</option>
                <option value="test:pending">{t.outcomePending}</option>
              </select>
            </label>
          </section>

          <section className="checkout-step" aria-labelledby="step-review">
            <h2 id="step-review" className="t-h3"><span aria-hidden="true">3</span>{t.review}</h2>
            {pendingPayment ? <div className="checkout-error" role="status"><strong>{t.verifyTitle}</strong><span>{t.verifyBody}</span><Button type="button" onClick={() => void completeAuthentication()} busy={submitting}>{t.verify}</Button></div> : null}
            {TURNSTILE_SITE_KEY ? <Turnstile siteKey={TURNSTILE_SITE_KEY} widgetRef={turnstileWidget} /> : null}
            {error ? <div className="checkout-error" role="alert"><strong>{t.errorTitle}</strong><span>{error}</span></div> : null}
            <Button size="large" className="checkout-pay" busy={submitting} type="submit" disabled={!!pendingPayment}>{submitting ? t.placing : format(t.pay, { amount: isolate(total) })}</Button>
            <p className="t-meta checkout-assurance">{t.assurance}</p>
          </section>
        </form>

        <aside className="order-summary" aria-labelledby="order-summary-title">
          <h2 id="order-summary-title" className="t-h3">{t.summary}</h2>
          <ul>
            {cart.items.map((item) => (
              <li key={item.variantId}>
                <div><ProductVisual compact slug={item.slug} title={item.title} /><b>{item.quantity}</b></div>
                <span><strong lang={CATALOG_LANG} dir="auto">{item.title}</strong><small lang={CATALOG_LANG} dir="auto">{item.variant}</small><small>{format(t.quantity, { count: item.quantity })}</small></span>
                <strong>{formatMoney(item.lineTotal, intl)}</strong>
              </li>
            ))}
          </ul>
          <div className="order-summary__total"><span>{dictionary.cart.subtotal}</span><strong>{total}</strong></div>
          <p className="t-meta">{t.summaryNote}</p>
        </aside>
      </div>
    </main>
  );
}
