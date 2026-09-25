"use client";

import { ProductCard } from "@/components/product/product-card";
import type { ProductCardModel } from "@/lib/types";
import { useRequireSignIn } from "@/components/shell/sign-in-gate";
import { useCartStore } from "@/store/cart-store";

/** `notes` maps product id to a short line shown on its card, e.g. why a related product is suggested. */
export function ProductGrid({ products, className = "", notes }: { products: ProductCardModel[]; className?: string; notes?: Record<string, string> }) {
  const add = useCartStore((state) => state.add);
  const requireSignIn = useRequireSignIn();
  async function addOrAskToSignIn(variantId: string) {
    if (!requireSignIn("bag")) return false;
    await add(variantId);
    return true;
  }
  return <div className={`product-grid ${className}`}>{products.map((product, index) => <ProductCard key={product.id} product={product} onAdd={addOrAskToSignIn} priority={index < 4} note={notes?.[product.id]} />)}</div>;
}
