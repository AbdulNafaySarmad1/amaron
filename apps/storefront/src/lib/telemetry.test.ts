import { beforeEach, describe, expect, it } from "vitest";
import { dataLayerProvider, itemFrom, registerTelemetryProvider, toGa4, track, type CommerceEvent } from "./telemetry";

describe("telemetry", () => {
  const seen: CommerceEvent[] = [];
  beforeEach(() => { seen.length = 0; });

  it("keeps shopping working when a provider throws", () => {
    registerTelemetryProvider({ name: "broken", send: () => { throw new Error("vendor script failed"); } });
    registerTelemetryProvider({ name: "recorder", send: (e) => { seen.push(e); } });
    expect(() => track({ name: "search", search_term: "lamp" })).not.toThrow();
    expect(seen).toEqual([{ name: "search", search_term: "lamp" }]);
  });

  it("maps to GA4 ecommerce events", () => {
    const item = itemFrom({ id: "p1", title: "French Press", brand: "Harbor", price: { amount: 59.24 }, quantity: 1 }, 0);
    expect(toGa4({ name: "add_to_cart", currency: "USD", value: 59.24, items: [item] })).toEqual({
      event: "add_to_cart",
      ecommerce: { currency: "USD", value: 59.24, items: [{ item_id: "p1", item_name: "French Press", item_brand: "Harbor", price: 59.24, quantity: 1, index: 0 }] },
    });
    expect(toGa4({ name: "view_item_list", list: "Books", items: [] })).toEqual({ event: "view_item_list", ecommerce: { item_list_name: "Books", items: [] } });
    expect(toGa4({ name: "search", search_term: "kettle" })).toEqual({ event: "search", search_term: "kettle" });
  });

  it("clears the previous ecommerce object before each ecommerce push", () => {
    const layer: Record<string, unknown>[] = [];
    (globalThis as { window?: unknown }).window = { dataLayer: layer };
    dataLayerProvider.send({ name: "purchase", transaction_id: "AM-1", currency: "USD", value: 10, items: [] });
    expect(layer).toEqual([{ ecommerce: null }, { event: "purchase", ecommerce: { transaction_id: "AM-1", currency: "USD", value: 10, items: [] } }]);
    delete (globalThis as { window?: unknown }).window;
  });
});
