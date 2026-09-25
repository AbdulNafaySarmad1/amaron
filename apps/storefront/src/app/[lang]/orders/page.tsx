import type { Metadata } from "next";
import { OrderList } from "@/components/orders/order-list";
import { currentDictionary, redirectToLogin } from "@/i18n/server";
import { publicSession } from "@/lib/auth/session";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await currentDictionary()).orders.title, robots: { index: false } };
}

export default async function OrdersPage() {
  if (!await publicSession()) await redirectToLogin("/orders");
  const t = await currentDictionary();
  return (
    <main className="page orders-page">
      <header className="page__header">
        <h1 className="t-h1">{t.orders.title}</h1>
        <p className="t-body page__lede">{t.orders.lede}</p>
      </header>
      <OrderList />
    </main>
  );
}
