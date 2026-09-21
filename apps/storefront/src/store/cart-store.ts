import { create } from "zustand";
import { browserRequest } from "@/lib/api";
import type { Cart, CartMutation } from "@/lib/types";

type CartState = {
  cart: Cart | null;
  status: "idle" | "loading" | "ready" | "error";
  isOpen: boolean;
  isLoading: boolean;
  error: string | null;
  reset: () => void;
  load: () => Promise<void>;
  open: () => Promise<void>;
  close: () => void;
  add: (variantId: string) => Promise<void>;
  setQuantity: (variantId: string, quantity: number) => Promise<void>;
  remove: (variantId: string) => Promise<void>;
};

let loadPromise: Promise<void> | null = null;
let cartRevision = 0;
let mutationQueue: Promise<unknown> = Promise.resolve();

function serializeMutation<T>(operation: () => Promise<T>): Promise<T> {
  const next = mutationQueue.then(operation, operation);
  mutationQueue = next.then(() => undefined, () => undefined);
  return next;
}

export const useCartStore = create<CartState>((set, get) => ({
  cart: null,
  status: "idle",
  isOpen: false,
  isLoading: false,
  error: null,
  reset: () => {
    cartRevision++;
    loadPromise = null;
    set({ cart: null, status: "idle", isOpen: false, isLoading: false, error: null });
  },
  load: () => {
    if (loadPromise) return loadPromise;
    const revision = cartRevision;
    set({ isLoading: true, status: "loading", error: null });
    loadPromise = browserRequest<Cart>("/api/bff/cart")
      .then((cart) => { if (revision === cartRevision) set({ cart, status: "ready" }); })
      .catch((error: unknown) => { if (revision === cartRevision) set({ status: "error", error: error instanceof Error ? error.message : "The cart could not be loaded." }); })
      .finally(() => { loadPromise = null; set({ isLoading: false }); });
    return loadPromise;
  },
  open: async () => {
    set({ isOpen: true });
    if (get().status !== "ready") await get().load();
  },
  close: () => set({ isOpen: false }),
  add: (variantId) => serializeMutation(async () => {
    if (get().status !== "ready") await get().load();
    if (get().status !== "ready") throw new Error(get().error ?? "The cart could not be loaded.");
    const current = get().cart?.items.find((item) => item.variantId === variantId)?.quantity ?? 0;
    cartRevision++;
    set({ isLoading: true, error: null });
    try {
      const mutation = await browserRequest<CartMutation>("/api/bff/cart/items", { method: "PUT", body: JSON.stringify({ variantId, quantity: Math.min(current + 1, 99) }) });
      const cart = get().cart;
      if (!cart || !mutation.changedItem) { await get().load(); return; }
      const existing = cart.items.some((item) => item.variantId === variantId);
      set({ cart: { cartId: mutation.cartId, totalQuantity: mutation.totalQuantity, subtotal: mutation.subtotal, version: mutation.version, items: existing ? cart.items.map((item) => item.variantId === variantId ? mutation.changedItem! : item) : [...cart.items, mutation.changedItem] }, status: "ready" });
    } finally { set({ isLoading: false }); }
  }),
  setQuantity: (variantId, quantity) => {
    if (quantity < 1) return get().remove(variantId);
    return serializeMutation(async () => {
    cartRevision++;
    set({ isLoading: true, error: null });
    try {
      const mutation = await browserRequest<CartMutation>("/api/bff/cart/items", { method: "PUT", body: JSON.stringify({ variantId, quantity }) });
      const cart = get().cart;
      if (cart && mutation.changedItem) {
        set({ cart: { ...cart, totalQuantity: mutation.totalQuantity, subtotal: mutation.subtotal, version: mutation.version, items: cart.items.map((item) => item.variantId === variantId ? mutation.changedItem! : item) }, status: "ready" });
      }
    } catch (error) {
      set({ error: error instanceof Error ? error.message : "That quantity could not be updated." });
    } finally {
      set({ isLoading: false });
    }
    });
  },
  remove: (variantId) => serializeMutation(async () => {
    cartRevision++;
    set({ isLoading: true, error: null });
    try {
      set({ cart: await browserRequest<Cart>(`/api/bff/cart/items/${encodeURIComponent(variantId)}`, { method: "DELETE" }), status: "ready" });
    } catch (error) {
      set({ error: error instanceof Error ? error.message : "That item could not be removed." });
    } finally {
      set({ isLoading: false });
    }
  }),
}));
