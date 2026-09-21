import { OrderDetail } from "@/components/orders/order-detail";
import { redirect } from "next/navigation";
import { publicSession } from "@/lib/auth/session";

export default async function OrderPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ placed?: string }> }) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  if (!await publicSession()) redirect(`/api/auth/login?returnTo=${encodeURIComponent(`/orders/${id}`)}`);
  return <OrderDetail id={id} placed={query.placed === "1"} />;
}
