/**
 * Where advertising may ever appear. Checkout, payment, cart and product-decision placements are deliberately absent,
 * so an ad there is a type error rather than a review comment.
 */
export type AdPlacement = "editorial-after-section" | "category-content";

const enabled = new Set((process.env.NEXT_PUBLIC_AD_PLACEMENTS ?? "").split(",").map((p) => p.trim()).filter(Boolean));

/** Disabled by default. When a placement is enabled, reserves labelled space for a provider to fill (no layout shift). */
export function AdSlot({ placement, label }: { placement: AdPlacement; label: string }) {
  if (!enabled.has(placement)) return null;
  return <aside className="ad-slot" aria-label={label} data-ad-placement={placement}><span className="t-meta">{label}</span></aside>;
}
