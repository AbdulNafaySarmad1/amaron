"use client";

import { type KeyboardEvent, type MouseEvent, startTransition, useEffect, useRef, useState } from "react";
import { ArrowIcon, CheckIcon, ClockIcon, CloseIcon, SearchIcon } from "@/components/icons";
import { useLocale, useLocalizedRouter, useT } from "@/components/providers/locale-provider";
import { format } from "@/i18n/dictionary";
import { browserRequest } from "@/lib/api";
import { categoryPath } from "@/lib/categories";
import { clearRecentSearches, readRecentSearches, rememberSearch } from "@/lib/recent-searches";
import type { Category, Suggestion } from "@/lib/types";
import { useSearchMode } from "@/store/search-store";
import { track } from "@/lib/telemetry";
import { useCategoryScope } from "@/components/shell/site-header";

type Option = { id: string; label: string; hint?: string; group: string; href: string; term?: string; lang?: string };

const queryHref = (term: string) => `/search?q=${encodeURIComponent(term)}`;

/** Search as an interaction mode: the page recedes, the field takes focus, and results arrive in place. */
export function SearchMode({ categories }: { categories: Category[] }) {
  const t = useT();
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
    <dialog ref={dialog} className="search-mode" aria-label={t.search.label} onClose={close} onClick={onBackdrop}>
      {isOpen ? <SearchPanel categories={categories} close={close} /> : null}
    </dialog>
  );
}

function SearchPanel({ categories, close }: { categories: Category[]; close: () => void }) {
  const router = useLocalizedRouter();
  const t = useT();
  const locale = useLocale();
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<{ term: string; items: Suggestion[]; failed: boolean } | null>(null);
  const [active, setActive] = useState(-1);
  const [recent, setRecent] = useState(readRecentSearches);
  const scopeCategory = useCategoryScope(categories);
  const [scoped, setScoped] = useState(true);
  const scope = scoped ? scopeCategory : undefined;

  const term = query.trim();
  // Results are keyed by scope and term together, so a late response never shows under the wrong scope.
  const key = `${scope?.slug ?? ""}|${term}`;
  useEffect(() => {
    if (term.length < 2) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      browserRequest<Suggestion[]>(`/api/public/search/suggestions?q=${encodeURIComponent(term)}${scope ? `&category=${scope.slug}` : ""}&locale=${locale}`, { signal: controller.signal })
        .then((items) => setResults({ term: key, items, failed: false }))
        .catch(() => { if (!controller.signal.aborted) setResults({ term: key, items: [], failed: true }); });
    }, 140);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [term, key, scope, locale]);

  const typing = term.length >= 2;
  const current = typing && results?.term === key ? results : null;
  const submitHref = (value: string) => (scope ? `${categoryPath(scope.slug)}?q=${encodeURIComponent(value)}` : queryHref(value));
  const options: Option[] = typing
    ? [
        ...(current?.items.filter((x) => x.type === "product" && x.slug).map((x) => ({ id: `p-${x.slug}`, label: x.value, lang: x.locale, group: t.search.products, href: `/products/${x.slug}`, term })) ?? []),
        ...(current?.items.filter((x) => x.type === "category" && x.slug).map((x) => ({ id: `c-${x.slug}`, label: x.value, lang: x.locale, hint: t.search.browseHint, group: t.search.spaces, href: categoryPath(x.slug!) })) ?? []),
        ...(scope
          ? [{ id: "all", label: format(t.category.seeAllIn, { category: scope.name }), group: "all", href: submitHref(term), term }, { id: "everywhere", label: format(t.category.searchAllFor, { query: term }), group: "all", href: queryHref(term), term }]
          : [{ id: "all", label: format(t.search.seeAll, { query: term }), group: "all", href: queryHref(term), term }]),
      ]
    : [
        ...recent.map((x, index) => ({ id: `r-${index}`, label: x, group: t.search.recent, href: queryHref(x), term: x })),
        ...categories.filter((x) => !x.parentId).map((x) => ({ id: `c-${x.slug}`, label: x.name, lang: x.locale, hint: t.search.browseHint, group: t.search.browse, href: categoryPath(x.slug) })),
      ];
  const groups = [...new Set(options.map((x) => x.group))];

  function go(option: Pick<Option, "href" | "term">) {
    if (option.term) { setRecent(rememberSearch(option.term)); track({ name: "search", search_term: option.term }); }
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
      else if (term) go({ href: submitHref(term), term });
    }
  }

  const status = typing && !current ? t.search.loading
    : current?.failed ? t.search.unavailable
    : current && current.items.length === 0 ? (scope ? format(t.category.noResultsIn, { category: scope.name, query: term }) : `${format(t.search.empty, { query: term })} ${t.search.emptyHint}`) : "";
  const placeholder = scope ? format(t.category.searchIn, { category: scope.name }) : t.search.placeholder;

  return (
    <div className="search-mode__panel">
      <div className="search-mode__field">
        <SearchIcon />
        <input
          role="combobox"
          aria-label={placeholder}
          aria-expanded={options.length > 0}
          aria-controls="search-mode-options"
          aria-autocomplete="list"
          aria-activedescendant={options[active] ? `search-option-${options[active].id}` : undefined}
          autoComplete="off"
          enterKeyHint="search"
          placeholder={placeholder}
          value={query}
          onChange={(event) => { setQuery(event.target.value); setActive(-1); }}
          onKeyDown={onKeyDown}
        />
        <button type="button" className="icon-button" onClick={close} aria-label={t.search.close}><CloseIcon /></button>
      </div>
      {scopeCategory ? (
        <button type="button" className="search-mode__scope" aria-pressed={scoped} onClick={() => { setScoped((x) => !x); setActive(-1); }}>
          {scoped ? <CheckIcon width={16} height={16} /> : null}
          <span dir="auto">{format(t.category.within, { category: scopeCategory.name })}</span>
          {scoped ? <span className="sr-only">. {t.category.removeScope}</span> : null}
        </button>
      ) : null}

      <div id="search-mode-options" role="listbox" aria-label={t.search.suggestions} className="search-mode__results">
        {groups.map((group) => (
          <div role="group" aria-label={group === "all" ? t.search.allResults : group} key={group} className="search-mode__group">
            {group === "all" ? null : (
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
                <span lang={option.lang} dir="auto">{option.label}</span>
                {option.hint ? <small>{option.hint}</small> : <ArrowIcon className="flip-rtl" />}
              </div>
            ))}
          </div>
        ))}
      </div>
      <p className="search-mode__status" role="status">{status}</p>
      {!typing && recent.length ? <button type="button" className="text-button search-mode__clear" onClick={() => { clearRecentSearches(); setRecent([]); setActive(-1); }}>{t.search.clearRecent}</button> : null}
    </div>
  );
}
