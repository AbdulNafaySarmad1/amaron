import { OrderDetail } from "@/components/orders/order-detail";

export default async function OrderPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ placed?: string }> }) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  return <OrderDetail id={id} placed={query.placed === "1"} />;
}
