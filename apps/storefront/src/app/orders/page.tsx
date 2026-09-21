import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { OrderList } from "@/components/orders/order-list";
import { publicSession } from "@/lib/auth/session";

export const metadata: Metadata = { title: "Orders" };
export default async function OrdersPage() {
  if (!await publicSession()) redirect("/api/auth/login?returnTo=/orders");
  return <main className="orders-page"><header><p className="eyebrow">Order archive</p><h1>Your orders</h1><p>Receipts, status, and the details worth keeping.</p></header><OrderList /></main>;
}
