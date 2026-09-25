import { create } from "zustand";

type SearchModeState = { isOpen: boolean; open: () => void; close: () => void };

export const useSearchMode = create<SearchModeState>((set) => ({
  isOpen: false,
  open: () => set({ isOpen: true }),
  close: () => set({ isOpen: false }),
}));
