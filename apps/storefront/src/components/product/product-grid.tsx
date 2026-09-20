"use client";

import { ProductCard } from "@/components/product/product-card";
import type { ProductCardModel } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";

export function ProductGrid({ products, className = "" }: { products: ProductCardModel[]; className?: string }) {
  const add = useCartStore((state) => state.add);
  return <div className={`product-grid ${className}`}>{products.map((product, index) => <ProductCard key={product.id} product={product} onAdd={add} priority={index < 4} />)}</div>;
}
