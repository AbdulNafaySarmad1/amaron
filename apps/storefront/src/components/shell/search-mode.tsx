"use client";

import { useRouter } from "next/navigation";
import { type KeyboardEvent, type MouseEvent, startTransition, useEffect, useRef, useState } from "react";
import { ArrowIcon, ClockIcon, CloseIcon, SearchIcon } from "@/components/icons";
import { commerceCopy } from "@/content/commerce";
import { browserRequest } from "@/lib/api";
import { clearRecentSearches, readRecentSearches, rememberSearch } from "@/lib/recent-searches";
import type { Category, Suggestion } from "@/lib/types";
import { useSearchMode } from "@/store/search-store";

type Option = { id: string; label: string; hint?: string; group: string; href: string; term?: string };

const categoryHref = (slug: string) => `/search?category=${encodeURIComponent(slug)}`;
const queryHref = (term: string) => `/search?q=${encodeURIComponent(term)}`;

/** Search as an interaction mode: the page recedes, the field takes focus, and results arrive in place. */
export function SearchMode({ categories }: { categories: Category[] }) {
  const { isOpen, open, close } = useSearchMode();
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = dialog.current;
    if (!element) return;
    if (isOpen && !element.open) { element.showModal(); element.querySelector("input")?.focus(); }
    if (!isOpen && element.open) element.close();
  }, [isOpen]);

  // "/" or Ctrl/Cmd+K opens search from anywhere that is not already a text field.
  useEffect(() => {
    function onKey(event: globalThis.KeyboardEvent) {
      const typing = (event.target as HTMLElement | null)?.closest("input, textarea, select, [contenteditable='true']");
      if ((event.key === "/" && !typing) || (event.key.toLowerCase() === "k" && (event.metaKey || event.ctrlKey))) {
        event.preventDefault();
        open();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open]);

  function onBackdrop(event: MouseEvent<HTMLDialogElement>) {
    if (event.target === event.currentTarget) close();
  }

  // The panel mounts per opening, so it starts clean and reads device-local recents only in the browser.
  return (
    <dialog ref={dialog} className="search-mode" aria-label="Search the store" onClose={close} onClick={onBackdrop}>
      {isOpen ? <SearchPanel categories={categories} close={close} /> : null}
    </dialog>
  );
}

function SearchPanel({ categories, close }: { categories: Category[]; close: () => void }) {
  const router = useRouter();
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<{ term: string; items: Suggestion[]; failed: boolean } | null>(null);
  const [active, setActive] = useState(-1);
  const [recent, setRecent] = useState(readRecentSearches);

  const term = query.trim();
  useEffect(() => {
    if (term.length < 2) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      browserRequest<Suggestion[]>(`/api/public/search/suggestions?q=${encodeURIComponent(term)}`, { signal: controller.signal })
        .then((items) => setResults({ term, items, failed: false }))
        .catch(() => { if (!controller.signal.aborted) setResults({ term, items: [], failed: true }); });
    }, 140);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [term]);

  const typing = term.length >= 2;
  const current = typing && results?.term === term ? results : null;
  const options: Option[] = typing
    ? [
        ...(current?.items.filter((x) => x.type === "product" && x.slug).map((x) => ({ id: `p-${x.slug}`, label: x.value, group: "Products", href: `/products/${x.slug}`, term })) ?? []),
        ...(current?.items.filter((x) => x.type === "category" && x.slug).map((x) => ({ id: `c-${x.slug}`, label: x.value, hint: "Browse", group: "Spaces", href: categoryHref(x.slug!) })) ?? []),
        { id: "all", label: commerceCopy.search.seeAll(term), group: "All", href: queryHref(term), term },
      ]
    : [
        ...recent.map((x, index) => ({ id: `r-${index}`, label: x, group: commerceCopy.search.recent, href: queryHref(x), term: x })),
        ...categories.filter((x) => !x.parentId).map((x) => ({ id: `c-${x.slug}`, label: x.name, hint: "Browse", group: commerceCopy.search.browse, href: categoryHref(x.slug) })),
      ];
  const groups = [...new Set(options.map((x) => x.group))];

  function go(option: Pick<Option, "href" | "term">) {
    if (option.term) setRecent(rememberSearch(option.term));
    close();
    startTransition(() => router.push(option.href));
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" && options.length) { event.preventDefault(); setActive((x) => (x + 1) % options.length); }
    else if (event.key === "ArrowUp" && options.length) { event.preventDefault(); setActive((x) => (x <= 0 ? options.length - 1 : x - 1)); }
    else if (event.key === "Enter") {
      event.preventDefault();
      const chosen = options[active];
      if (chosen) go(chosen);
      else if (term) go({ href: queryHref(term), term });
    }
  }

  const status = typing && !current ? commerceCopy.search.loading
    : current?.failed ? "Suggestions are unavailable right now. Press Enter to search anyway."
    : current && current.items.length === 0 ? `${commerceCopy.search.empty(term)} ${commerceCopy.search.emptyHint}` : "";

  return (
    <div className="search-mode__panel">
      <div className="search-mode__field">
        <SearchIcon />
        <input
          role="combobox"
          aria-label={commerceCopy.search.placeholder}
          aria-expanded={options.length > 0}
          aria-controls="search-mode-options"
          aria-autocomplete="list"
          aria-activedescendant={options[active] ? `search-option-${options[active].id}` : undefined}
          autoComplete="off"
          enterKeyHint="search"
          placeholder={commerceCopy.search.placeholder}
          value={query}
          onChange={(event) => { setQuery(event.target.value); setActive(-1); }}
          onKeyDown={onKeyDown}
        />
        <button type="button" className="icon-button" onClick={close} aria-label="Close search"><CloseIcon /></button>
      </div>

      <div id="search-mode-options" role="listbox" aria-label="Suggestions" className="search-mode__results">
        {groups.map((group) => (
          <div role="group" aria-label={group === "All" ? "All results" : group} key={group} className="search-mode__group">
            {group === "All" ? null : (
              <p className="t-meta search-mode__group-label" aria-hidden="true">{group}</p>
            )}
            {options.map((option, index) => option.group !== group ? null : (
              <div
                key={option.id}
                id={`search-option-${option.id}`}
                role="option"
                aria-selected={index === active}
                className={`search-mode__option ${option.id === "all" ? "search-mode__option--all" : ""}`}
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => go(option)}
                onMouseEnter={() => setActive(index)}
              >
                {option.id.startsWith("r-") ? <ClockIcon /> : null}
                <span>{option.label}</span>
                {option.hint ? <small>{option.hint}</small> : <ArrowIcon className="flip-rtl" />}
              </div>
            ))}
          </div>
        ))}
      </div>
      <p className="search-mode__status" role="status">{status}</p>
      {!typing && recent.length ? <button type="button" className="text-button search-mode__clear" onClick={() => { clearRecentSearches(); setRecent([]); setActive(-1); }}>{commerceCopy.search.clearRecent} recent searches</button> : null}
    </div>
  );
}
