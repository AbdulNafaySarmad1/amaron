"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { CheckIcon } from "@/components/icons";
import { browserRequest, formatMoney } from "@/lib/api";
import type { Order } from "@/lib/types";

export function OrderDetail({ id, placed }: { id: string; placed: boolean }) {
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => { browserRequest<Order>(`/api/bff/orders/${encodeURIComponent(id)}`).then(setOrder).catch(() => setError(true)); }, [id]);
  if (error) return <main className="order-detail orders-state"><h1>We couldn&apos;t find that order.</h1><Link className="text-link" href="/orders">Back to your orders</Link></main>;
  if (!order) return <main className="order-detail orders-state"><p>Preparing your order details…</p></main>;
  return <main className="order-detail">
    {placed ? <div className="order-confirmed"><span><CheckIcon /></span><div><p className="eyebrow">Order confirmed</p><h1>It&apos;s officially on the list.</h1><p>We&apos;ve reserved your items and started preparing them.</p></div></div> : null}
    <header><div><p className="eyebrow">{order.status}</p><h1>{order.orderNumber}</h1><p>Placed {new Intl.DateTimeFormat("en-US", { dateStyle: "long", timeStyle: "short" }).format(new Date(order.createdAt))}</p></div><Link className="text-link" href="/orders">All orders</Link></header>
    <section className="order-receipt"><h2>Order details</h2><ul>{order.items.map((item) => <li key={item.variantId}><div><strong>{item.productTitle}</strong><span>{item.variantName} · Qty {item.quantity}</span><small>{item.sku}</small></div><strong>{formatMoney(item.lineTotal)}</strong></li>)}</ul><div><span>Subtotal</span><strong>{formatMoney(order.subtotal)}</strong></div></section>
    <Link className="button button--secondary button--medium" href="/search">Keep browsing</Link>
  </main>;
}
