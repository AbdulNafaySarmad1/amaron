import { Untranslated } from "@/components/i18n/untranslated";
import type { Metadata } from "next";
import { CheckoutForm } from "@/components/checkout/checkout-form";
import { redirectToLogin } from "@/i18n/server";
import { publicSession } from "@/lib/auth/session";

export const metadata: Metadata = { title: "Checkout", robots: { index: false } };
export default async function CheckoutPage() {
  if (!await publicSession()) await redirectToLogin("/checkout");
  return <Untranslated><CheckoutForm /></Untranslated>;
}
