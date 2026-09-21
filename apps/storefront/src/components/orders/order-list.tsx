"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { browserRequest, formatMoney } from "@/lib/api";
import type { Order } from "@/lib/types";

export function OrderList() {
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => { browserRequest<Order[]>("/api/bff/orders").then(setOrders).catch(() => setError(true)); }, []);
  if (error) return <div className="orders-state"><h2>We couldn&apos;t load your orders.</h2><p>Try refreshing the page.</p></div>;
  if (!orders) return <div className="orders-state"><p>Looking up your orders…</p></div>;
  if (!orders.length) return <div className="orders-state"><span>0</span><h2>No orders yet</h2><p>When you place one, you&apos;ll find every detail here.</p><Link className="button button--primary button--medium" href="/search">Browse goods</Link></div>;
  return <div className="order-list">{orders.map((order) => <Link className="order-card" key={order.id} href={`/orders/${order.id}`}><div><span className="eyebrow">{order.status}</span><h2>{order.orderNumber}</h2><p>{new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(order.createdAt))} · {order.items.length} lines</p></div><strong>{formatMoney(order.subtotal)}</strong><span>View order →</span></Link>)}</div>;
}
