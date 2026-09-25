import { Untranslated } from "@/components/i18n/untranslated";
import type { Metadata } from "next";
import { OrderList } from "@/components/orders/order-list";
import { redirectToLogin } from "@/i18n/server";
import { publicSession } from "@/lib/auth/session";

export const metadata: Metadata = { title: "Orders", robots: { index: false } };
export default async function OrdersPage() {
  if (!await publicSession()) await redirectToLogin("/orders");
  return <Untranslated><main className="orders-page"><header><p className="eyebrow">Order archive</p><h1>Your orders</h1><p>Receipts, status, and the details worth keeping.</p></header><OrderList /></main></Untranslated>;
}
