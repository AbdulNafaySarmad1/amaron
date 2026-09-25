/**
 * CommerceTelemetry: the only place commerce events are emitted. Components call track(); providers (GTM/GA4 today)
 * translate events for their vendor. With no provider registered, tracking is a no-op, and a failing provider can never
 * break shopping.
 */
export type CommerceItem = { item_id: string; item_name: string; item_brand?: string; item_category?: string; item_variant?: string; price?: number; quantity?: number; index?: number };

type Priced = { currency: string; value: number; items: CommerceItem[] };
export type CommerceEvent =
  | { name: "view_item_list"; list: string; items: CommerceItem[] }
  | { name: "select_item"; list: string; items: CommerceItem[] }
  | ({ name: "view_item" | "add_to_cart" | "remove_from_cart" | "view_cart" | "begin_checkout" } & Priced)
  | ({ name: "add_shipping_info"; shipping_tier?: string } & Priced)
  | ({ name: "add_payment_info"; payment_type: string } & Priced)
  | ({ name: "purchase"; transaction_id: string } & Priced)
  | { name: "add_to_wishlist"; items: CommerceItem[] }
  | { name: "search"; search_term: string };

export type TelemetryProvider = { name: string; send: (event: CommerceEvent) => void };

const providers: TelemetryProvider[] = [];

export function registerTelemetryProvider(provider: TelemetryProvider) {
  if (!providers.some((p) => p.name === provider.name)) providers.push(provider);
}

export function track(event: CommerceEvent) {
  for (const provider of providers) {
    try { provider.send(event); } catch { /* analytics must never break commerce */ }
  }
}

/** GA4 recommended-event shape for Google Tag Manager's dataLayer. */
export function toGa4(event: CommerceEvent): Record<string, unknown> {
  if (event.name === "search") return { event: "search", search_term: event.search_term };
  const { name, ...rest } = event;
  const ecommerce = "list" in rest ? { item_list_name: rest.list, items: rest.items } : rest;
  return { event: name, ecommerce };
}

type DataLayerWindow = Window & { dataLayer?: Record<string, unknown>[] };

export const dataLayerProvider: TelemetryProvider = {
  name: "gtm",
  send(event) {
    const w = window as DataLayerWindow;
    w.dataLayer ??= [];
    // GA4 guidance: clear the previous ecommerce object so fields never leak between events.
    if (event.name !== "search") w.dataLayer.push({ ecommerce: null });
    w.dataLayer.push(toGa4(event));
  },
};

/** Card or line data in GA4 item form. */
export const itemFrom = (p: { id: string; title: string; brand?: string; price?: { amount: number }; quantity?: number; variant?: string }, index?: number): CommerceItem => ({
  item_id: p.id,
  item_name: p.title,
  ...(p.brand ? { item_brand: p.brand } : {}),
  ...(p.variant ? { item_variant: p.variant } : {}),
  ...(p.price ? { price: p.price.amount } : {}),
  ...(p.quantity ? { quantity: p.quantity } : {}),
  ...(index !== undefined ? { index } : {}),
});
