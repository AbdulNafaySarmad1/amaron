import { create } from "zustand";
import { createJSONStorage, persist, type StateStorage } from "zustand/middleware";

// ponytail: device-local saved list; move to an account-backed wishlist API when saved items must follow the shopper.
export const SAVED_LIMIT = 50; // matches the catalog batch endpoint limit

type SavedState = {
  ids: string[];
  hydrated: boolean;
  toggle: (productId: string) => void;
};

// Storage can throw in private windows or with blocked site data; saving then just lasts for the visit.
const safeStorage: StateStorage = {
  getItem: (name) => { try { return localStorage.getItem(name); } catch { return null; } },
  setItem: (name, value) => { try { localStorage.setItem(name, value); } catch { /* keep in memory */ } },
  removeItem: (name) => { try { localStorage.removeItem(name); } catch { /* ignore */ } },
};

export const useSavedStore = create<SavedState>()(persist(
  (set) => ({
    ids: [],
    hydrated: false,
    toggle: (productId) => set(({ ids }) => ({ ids: ids.includes(productId) ? ids.filter((id) => id !== productId) : [productId, ...ids].slice(0, SAVED_LIMIT) })),
  }),
  {
    name: "amaron.saved",
    storage: createJSONStorage(() => safeStorage),
    partialize: ({ ids }) => ({ ids }),
    skipHydration: true,
    onRehydrateStorage: () => () => useSavedStore.setState({ hydrated: true }),
  },
));
