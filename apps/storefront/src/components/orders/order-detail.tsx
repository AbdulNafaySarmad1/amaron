"use client";

import { useEffect, useState } from "react";
import { CheckIcon } from "@/components/icons";
import { Link, useIntlLocale, useT } from "@/components/providers/locale-provider";
import { CATALOG_LANG } from "@/i18n/config";
import { format } from "@/i18n/dictionary";
import { browserRequest, formatMoney } from "@/lib/api";
import type { Order } from "@/lib/types";
import { useOrderStatus } from "./order-list";

/** Reassurance first: the order is confirmed and what happens next. The receipt follows. No selling here. */
export function OrderDetail({ id, placed, paymentConfirmed }: { id: string; placed: boolean; paymentConfirmed: boolean }) {
  const t = useT();
  const intl = useIntlLocale();
  const statusLabel = useOrderStatus();
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => { browserRequest<Order>(`/api/bff/orders/${encodeURIComponent(id)}`).then(setOrder).catch(() => setError(true)); }, [id]);

  if (error) return <main className="page order-page"><div className="empty-state" role="alert"><h1 className="t-h2">{t.orders.notFound}</h1><Link className="button button--secondary button--medium" href="/orders">{t.orders.back}</Link></div></main>;
  if (!order) return <main className="page order-page"><p className="t-meta" role="status">{t.orders.loadingOne}</p></main>;

  const confirmed = placed || paymentConfirmed;
  return (
    <main className="page order-page">
      {confirmed ? (
        <section className="order-confirmed" aria-labelledby="order-confirmed-title">
          <span className="order-confirmed__mark"><CheckIcon /></span>
          <div>
            <h1 id="order-confirmed-title" className="t-h1">{t.orders.confirmedTitle}</h1>
            <p className="t-body">{paymentConfirmed ? t.orders.paymentConfirmed : t.orders.confirmedBody}</p>
            <p className="t-meta">{format(t.orders.number, { number: order.orderNumber })}</p>
          </div>
        </section>
      ) : (
        <header className="page__header">
          <p className="t-caption">{statusLabel(order.status)}</p>
          <h1 className="t-h1">{format(t.orders.number, { number: order.orderNumber })}</h1>
        </header>
      )}

      {confirmed ? (
        <section className="order-next" aria-labelledby="order-next-title">
          <h2 id="order-next-title" className="t-h3">{t.orders.nextTitle}</h2>
          <ol><li>{t.orders.nextPrepare}</li><li>{t.orders.nextStatus}</li></ol>
        </section>
      ) : null}

      <section className="order-receipt" aria-labelledby="order-receipt-title">
        <div className="order-receipt__head">
          <h2 id="order-receipt-title" className="t-h3">{t.orders.details}</h2>
          <p className="t-meta">{statusLabel(order.status)} · {format(t.orders.placedOn, { date: new Intl.DateTimeFormat(intl, { dateStyle: "long", timeStyle: "short" }).format(new Date(order.createdAt)) })}</p>
        </div>
        <ul>
          {order.items.map((item) => (
            <li key={item.variantId}>
              <div><strong lang={CATALOG_LANG} dir="auto">{item.productTitle}</strong><span><span lang={CATALOG_LANG} dir="auto">{item.variantName}</span> · {format(t.checkout.quantity, { count: item.quantity })}</span><small>{item.sku}</small></div>
              <strong>{formatMoney(item.lineTotal, intl)}</strong>
            </li>
          ))}
        </ul>
        <div className="order-receipt__total"><span>{t.cart.subtotal}</span><strong>{formatMoney(order.subtotal, intl)}</strong></div>
      </section>

      <div className="order-page__actions">
        <Link className="button button--primary button--medium" href="/">{t.orders.keepBrowsing}</Link>
        <Link className="button button--secondary button--medium" href="/orders">{t.orders.all}</Link>
      </div>
    </main>
  );
}
