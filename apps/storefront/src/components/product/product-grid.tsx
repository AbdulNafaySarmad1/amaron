"use client";

import { ProductCard } from "@/components/product/product-card";
import { useStorefrontSession } from "@/components/providers/storefront-provider";
import type { ProductCardModel } from "@/lib/types";
import { useRouter } from "next/navigation";
import { useCartStore } from "@/store/cart-store";

export function ProductGrid({ products, className = "" }: { products: ProductCardModel[]; className?: string }) {
  const add = useCartStore((state) => state.add);
  const session = useStorefrontSession();
  const router = useRouter();
  async function addOrLogin(variantId: string) {
    if (!session.authenticated) {
      router.push(`/api/auth/login?returnTo=${encodeURIComponent(window.location.pathname + window.location.search)}`);
      return;
    }
    await add(variantId);
  }
  return <div className={`product-grid ${className}`}>{products.map((product, index) => <ProductCard key={product.id} product={product} onAdd={addOrLogin} priority={index < 4} />)}</div>;
}
