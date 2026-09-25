"use client";

import { SearchIcon } from "@/components/icons";
import { useT } from "@/components/providers/locale-provider";
import { useSearchMode } from "@/store/search-store";

/** The homepage's main affordance: one obvious field that opens search mode. */
export function HomeSearch() {
  const t = useT();
  const open = useSearchMode((state) => state.open);
  return (
    <button type="button" className="home-search" onClick={open} aria-haspopup="dialog">
      <SearchIcon />
      <span>{t.search.placeholder}</span>
      <kbd aria-hidden="true">/</kbd>
    </button>
  );
}
