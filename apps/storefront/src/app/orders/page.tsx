import type { Metadata } from "next";
import { OrderList } from "@/components/orders/order-list";

export const metadata: Metadata = { title: "Orders" };
export default function OrdersPage() { return <main className="orders-page"><header><p className="eyebrow">Order archive</p><h1>Your orders</h1><p>Receipts, status, and the details worth keeping.</p></header><OrderList /></main>; }
