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
  kind: "headphones",
  locale: "en",
  highlights: [{ label: "Type", value: "Over-ear" }, { label: "Noise cancelling", value: "Adaptive ANC" }, { label: "Battery", value: "30 hours" }],
};

const typeRoles = [
  ["t-display", "Display", "Find something worth keeping", "One moment per page, never for ordinary headings."],
  ["t-h1", "H1", "Refrigerators", "Page titles: category, product, checkout."],
  ["t-h2", "H2", "Complete your setup", "Section titles."],
  ["t-h3", "H3", "Before it arrives", "Sub-sections and panel titles."],
  ["t-product", "Product title", "Frost 500L French-Door Refrigerator", "Product names in lists and cards."],
  ["t-ui", "UI heading", "Delivery and returns", "Labels for controls, menus, form groups."],
  ["t-body", "Body", "Arrives Tuesday. Free returns within 30 days.", "Running text."],
  ["t-meta", "Metadata", "4.7 · 1,284 reviews · In stock", "Secondary facts."],
  ["t-caption", "Caption", "Editor's pick", "Tiny labels and eyebrows."],
] as const;

const colorTokens = ["--canvas", "--surface", "--ink", "--ink-soft", "--ink-faint", "--accent", "--accent-deep", "--success", "--danger", "--focus"];

export default function DesignSystemPage() {
  return (
    <main className="design-review">
      <header><p className="t-caption">Amaron design system</p><h1 className="t-h1">Foundations</h1><p className="t-body">Tokens live in <code>globals.css</code>. Layout uses logical properties throughout, so every component works right-to-left.</p></header>

      <section className="design-review__section" aria-labelledby="type-heading">
        <h2 id="type-heading" className="t-h2">Type roles</h2>
        <dl className="design-review__type">
          {typeRoles.map(([className, role, sample, usage]) => (
            <div key={className}><dt className="t-meta">{role} · <code>.{className}</code><br />{usage}</dt><dd className={className}>{sample}</dd></div>
          ))}
        </dl>
      </section>

      <section className="design-review__section" aria-labelledby="color-heading">
        <h2 id="color-heading" className="t-h2">Surfaces and ink</h2>
        <ul className="design-review__swatches">
          {colorTokens.map((token) => <li key={token}><span style={{ background: `var(${token})` }} /><code className="t-meta">{token}</code></li>)}
        </ul>
      </section>

      <section className="design-review__section" aria-labelledby="card-heading">
        <h2 id="card-heading" className="t-h2">Product card</h2>
        <div className="design-review__grid">
          <div><span className="state-label">Interactive</span><ProductCard product={reference} onAdd={async () => { await new Promise((resolve) => window.setTimeout(resolve, 650)); }} priority /></div>
          <div><span className="state-label">Unavailable</span><ProductCard product={{ ...reference, id: "unavailable", slug: "linen-speaker", title: "Linen Room Speaker", availabilityHint: "out_of_stock", listPrice: null }} /></div>
        </div>
      </section>
    </main>
  );
}
