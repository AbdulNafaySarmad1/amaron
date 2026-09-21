"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { AnimatePresence, motion } from "motion/react";
import { FormEvent, KeyboardEvent, startTransition, useDeferredValue, useEffect, useState } from "react";
import { CartIcon, SearchIcon } from "@/components/icons";
import { useStorefrontSession } from "@/components/providers/storefront-provider";
import { commerceCopy } from "@/content/commerce";
import { browserRequest } from "@/lib/api";
import { motionTokens } from "@/lib/motion";
import type { Category, Suggestion } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";

export function SiteHeader({ categories }: { categories: Category[] }) {
  const router = useRouter();
  const session = useStorefrontSession();
  const openCart = useCartStore((state) => state.open);
  const cartCount = useCartStore((state) => state.cart?.totalQuantity ?? 0);
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);
  const [active, setActive] = useState(-1);
  const [focused, setFocused] = useState(false);

  useEffect(() => {
    if (deferredQuery.trim().length < 2) return;
    const controller = new AbortController();
    browserRequest<Suggestion[]>(`/api/public/search/suggestions?q=${encodeURIComponent(deferredQuery.trim())}`, { signal: controller.signal })
      .then(setSuggestions)
      .catch((error) => { if (error instanceof DOMException && error.name === "AbortError") return; setSuggestions([]); });
    return () => controller.abort();
  }, [deferredQuery]);

  function submit(event: FormEvent) {
    event.preventDefault();
    const clean = query.trim();
    if (!clean) return;
    setFocused(false);
    startTransition(() => router.push(`/search?q=${encodeURIComponent(clean)}`));
  }

  function choose(suggestion: Suggestion) {
    setQuery(suggestion.value);
    setFocused(false);
    startTransition(() => router.push(suggestion.type === "product" && suggestion.slug ? `/products/${suggestion.slug}` : `/search?category=${suggestion.slug ?? ""}`));
  }

  function navigateSuggestions(event: KeyboardEvent<HTMLInputElement>) {
    if (!suggestions.length) return;
    if (event.key === "ArrowDown") { event.preventDefault(); setActive((value) => (value + 1) % suggestions.length); }
    if (event.key === "ArrowUp") { event.preventDefault(); setActive((value) => value <= 0 ? suggestions.length - 1 : value - 1); }
    if (event.key === "Enter" && active >= 0) { event.preventDefault(); choose(suggestions[active]!); }
    if (event.key === "Escape") setFocused(false);
  }

  const showSuggestions = focused && query.length >= 2;
  async function logout() {
    const result = await browserRequest<{ logoutUrl: string }>("/api/auth/logout", { method: "POST" });
    window.location.assign(result.logoutUrl);
  }
  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link className="wordmark" href="/" aria-label="Amaron home"><span>A</span>maron</Link>
        <nav className="primary-nav" aria-label="Primary navigation">
          {categories.slice(0, 4).map((category) => <Link key={category.id} href={`/search?category=${category.slug}`}>{category.name}</Link>)}
          <Link href="/search">All goods</Link>
          {session.authenticated ? <Link href="/orders">Orders</Link> : null}
        </nav>
        <form className="header-search" role="search" onSubmit={submit}>
          <SearchIcon />
          <label className="sr-only" htmlFor="site-search">Search the store</label>
          <input id="site-search" role="combobox" value={query} onChange={(event) => { setQuery(event.target.value); setActive(-1); if (event.target.value.trim().length < 2) setSuggestions([]); }} onFocus={() => setFocused(true)} onBlur={() => window.setTimeout(() => setFocused(false), 120)} onKeyDown={navigateSuggestions} placeholder={commerceCopy.search.placeholder} autoComplete="off" aria-autocomplete="list" aria-expanded={showSuggestions} aria-controls="search-suggestions" aria-activedescendant={active >= 0 ? `suggestion-${active}` : undefined} />
          <AnimatePresence>
            {showSuggestions ? (
              <motion.div id="search-suggestions" className="search-suggestions" role="listbox" initial={{ opacity: 0, y: -5 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -5 }} transition={{ duration: motionTokens.duration.quick }}>
                {suggestions.length ? suggestions.map((suggestion, index) => (
                  <button id={`suggestion-${index}`} role="option" aria-selected={index === active} type="button" key={`${suggestion.type}-${suggestion.value}`} onMouseDown={() => choose(suggestion)}>
                    <span>{suggestion.value}</span><small>{suggestion.type}</small>
                  </button>
                )) : <p>{commerceCopy.search.loading}</p>}
              </motion.div>
            ) : null}
          </AnimatePresence>
        </form>
        <div className="header-account">
          {session.authenticated ? <><span>Hello, {session.user.name ?? "there"}</span><Link className="text-button" href="/api/auth/account">Profile</Link><button className="text-button" type="button" onClick={() => void logout()}>Sign out</button><button className="cart-trigger" type="button" onClick={() => void openCart()} aria-label={`Open cart with ${cartCount} items`}><CartIcon /><span>Cart</span><motion.b key={cartCount} initial={{ scale: 0.72 }} animate={{ scale: 1 }} transition={motionTokens.spring.tactile}>{cartCount}</motion.b></button></> : <Link className="button button--primary button--small" href="/api/auth/login?returnTo=/">Sign in</Link>}
        </div>
      </div>
    </header>
  );
}
