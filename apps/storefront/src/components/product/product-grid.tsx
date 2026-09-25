"use client";

import { ProductCard } from "@/components/product/product-card";
import type { ProductCardModel } from "@/lib/types";
import { useRequireSignIn } from "@/components/shell/sign-in-gate";
import { useEffect } from "react";
import { itemFrom, track } from "@/lib/telemetry";
import { useCartStore } from "@/store/cart-store";

/** `notes` maps product id to a short line shown on its card, e.g. why a related product is suggested. */
/** `list` names the list for analytics (view_item_list / select_item); omit it for lists not worth measuring. */
export function ProductGrid({ products, className = "", notes, list }: { products: ProductCardModel[]; className?: string; notes?: Record<string, string>; list?: string }) {
  const key = products.map((p) => p.id).join(",");
  useEffect(() => {
    if (list && key) track({ name: "view_item_list", list, items: products.map((p, i) => itemFrom({ id: p.id, title: p.title, brand: p.brand, price: p.price }, i)) });
    // Tracks once per distinct set of products, not per render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [list, key]);
  const add = useCartStore((state) => state.add);
  const requireSignIn = useRequireSignIn();
  async function addOrAskToSignIn(variantId: string) {
    if (!requireSignIn("bag")) return false;
    await add(variantId);
    return true;
  }
  return <div className={`product-grid ${className}`}>{products.map((product, index) => <ProductCard key={product.id} product={product} onAdd={addOrAskToSignIn} priority={index < 4} note={notes?.[product.id]} onSelect={list ? () => track({ name: "select_item", list, items: [itemFrom({ id: product.id, title: product.title, brand: product.brand, price: product.price }, index)] }) : undefined} />)}</div>;
}
