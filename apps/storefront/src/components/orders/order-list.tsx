"use client";

import { useEffect, useState } from "react";
import { Link, useIntlLocale, useT } from "@/components/providers/locale-provider";
import { format, plural } from "@/i18n/dictionary";
import { browserRequest, formatMoney } from "@/lib/api";
import type { Order } from "@/lib/types";

/** Order statuses from the API, in the shopper's language. Unknown statuses show as sent rather than disappearing. */
export function useOrderStatus() {
  const t = useT().orders;
  const labels: Record<string, string> = { PendingPayment: t.statusPendingPayment, Confirmed: t.statusConfirmed, Placed: t.statusPlaced, Cancelled: t.statusCancelled };
  return (status: string) => labels[status] ?? status;
}

export function OrderList() {
  const t = useT();
  const intl = useIntlLocale();
  const statusLabel = useOrderStatus();
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => { browserRequest<Order[]>("/api/bff/orders").then(setOrders).catch(() => setError(true)); }, []);

  if (error) return <div className="empty-state" role="alert"><h2 className="t-h3">{t.orders.loadError}</h2><p>{t.orders.loadErrorBody}</p></div>;
  if (!orders) return <p className="t-meta" role="status">{t.orders.loading}</p>;
  if (!orders.length) return <div className="empty-state"><h2 className="t-h3">{t.orders.emptyTitle}</h2><p>{t.orders.emptyBody}</p><Link className="button button--primary button--medium" href="/">{t.cart.startShopping}</Link></div>;
  return (
    <ul className="order-list">
      {orders.map((order) => (
        <li key={order.id}>
          <Link className="order-card" href={`/orders/${order.id}`}>
            <div>
              <span className="t-caption">{statusLabel(order.status)}</span>
              <h2 className="t-h3">{format(t.orders.number, { number: order.orderNumber })}</h2>
              <p className="t-meta">{new Intl.DateTimeFormat(intl, { dateStyle: "medium" }).format(new Date(order.createdAt))} · {plural(t.orders.items, order.items.reduce((sum, item) => sum + item.quantity, 0), intl)}</p>
            </div>
            <strong>{formatMoney(order.subtotal, intl)}</strong>
            <span className="order-card__view">{t.orders.view}</span>
          </Link>
        </li>
      ))}
    </ul>
  );
}
