"use client";

import { useEffect } from "react";
import { CartDrawer } from "@/components/cart/cart-drawer";
import { useCartStore } from "@/store/cart-store";

export function StorefrontProvider({ children }: { children: React.ReactNode }) {
  const load = useCartStore((state) => state.load);
  useEffect(() => { void load(); }, [load]);
  return <>{children}<CartDrawer /></>;
}
