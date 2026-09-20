"use client";

import { ProductCard } from "@/components/product/product-card";
import type { ProductCardModel } from "@/lib/types";

const reference: ProductCardModel = {
  id: "reference",
  defaultVariantId: "reference-variant",
  slug: "studio-headphones",
  title: "Studio Arc Headphones",
  brand: "Northstar",
  primaryImage: null,
  price: { amount: 189, currency: "USD" },
  listPrice: { amount: 229, currency: "USD" },
  rating: 4.8,
  reviewCount: 384,
  availabilityHint: "in_stock",
  badges: ["featured"],
};

export default function DesignSystemPage() {
  return (
    <main className="design-review">
      <header><p className="eyebrow">Reference component 01</p><h1>Product card</h1><p>Warm editorial hierarchy, restrained mechanics, and complete interaction states.</p></header>
      <section className="design-review__grid" aria-label="Product card states">
        <div><span className="state-label">Interactive</span><ProductCard product={reference} onAdd={async () => { await new Promise((resolve) => window.setTimeout(resolve, 650)); }} priority /></div>
        <div><span className="state-label">Unavailable</span><ProductCard product={{ ...reference, id: "unavailable", slug: "linen-speaker", title: "Linen Room Speaker", availabilityHint: "out_of_stock", listPrice: null }} /></div>
      </section>
    </main>
  );
}
