import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { CheckoutForm } from "@/components/checkout/checkout-form";
import { publicSession } from "@/lib/auth/session";

export const metadata: Metadata = { title: "Checkout" };
export default async function CheckoutPage() {
  if (!await publicSession()) redirect("/api/auth/login?returnTo=/checkout");
  return <CheckoutForm />;
}
