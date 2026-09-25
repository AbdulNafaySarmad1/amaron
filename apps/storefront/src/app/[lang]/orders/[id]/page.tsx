import type { Metadata } from "next";
import { OrderDetail } from "@/components/orders/order-detail";
import { redirectToLogin } from "@/i18n/server";
import { publicSession } from "@/lib/auth/session";

export const metadata: Metadata = { robots: { index: false } };
export default async function OrderPage({ params, searchParams }: PageProps<"/[lang]/orders/[id]">) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  if (!await publicSession()) await redirectToLogin(`/orders/${encodeURIComponent(id)}`);
  return <OrderDetail id={id} placed={query.placed === "1"} paymentConfirmed={query.payment === "confirmed"} />;
}
