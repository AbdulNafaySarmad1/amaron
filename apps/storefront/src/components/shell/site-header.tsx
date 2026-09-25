"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { BagIcon, ChevronIcon, HeartIcon, SearchIcon, UserIcon } from "@/components/icons";
import { useStorefrontSession } from "@/components/providers/storefront-provider";
import { commerceCopy } from "@/content/commerce";
import { browserRequest } from "@/lib/api";
import type { Category } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";
import { useSavedStore } from "@/store/saved-store";
import { useSearchMode } from "@/store/search-store";

const categoryHref = (slug: string) => `/search?category=${encodeURIComponent(slug)}`;

function hidePopover(id: string) {
  document.getElementById(id)?.hidePopover();
}

export function SiteHeader({ categories }: { categories: Category[] }) {
  const pathname = usePathname();
  const session = useStorefrontSession();
  const openSearch = useSearchMode((state) => state.open);
  const savedCount = useSavedStore((state) => state.ids.length);
  const roots = categories.filter((category) => !category.parentId);

  async function logout() {
    const result = await browserRequest<{ logoutUrl: string }>("/api/auth/logout", { method: "POST" });
    window.location.assign(result.logoutUrl);
  }

  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link className="wordmark" href="/" aria-label="Amaron home"><span>A</span>maron</Link>

        <nav className="primary-nav" aria-label="Primary">
          <Link href="/search" aria-current={pathname === "/search" ? "page" : undefined}>Discover</Link>
          <button type="button" className="primary-nav__menu" popoverTarget="category-panel">Categories <ChevronIcon width={16} height={16} /></button>
        </nav>

        <button type="button" className="search-trigger" onClick={openSearch} aria-haspopup="dialog">
          <SearchIcon />
          <span>{commerceCopy.search.placeholder}</span>
          <kbd aria-hidden="true">/</kbd>
        </button>

        <div className="header-actions">
          <button type="button" className="header-action header-action--search" onClick={openSearch} aria-label={commerceCopy.search.trigger}><SearchIcon /></button>
          <Link className="header-action header-action--saved" href="/saved" aria-current={pathname === "/saved" ? "page" : undefined} aria-label={`Saved, ${savedCount} items`}>
            <HeartIcon /><span className="header-action__label">Saved</span>{savedCount ? <b>{savedCount}</b> : null}
          </Link>
          {session.authenticated ? (
            <>
              <button type="button" className="header-action" popoverTarget="account-panel" aria-label="Account"><UserIcon /><span className="header-action__label">Account</span></button>
              <div id="account-panel" popover="auto" className="header-popover header-popover--account">
                <p className="t-meta">Signed in as</p>
                <p className="t-ui">{session.user.name ?? "Your account"}</p>
                <Link href="/orders" onClick={() => hidePopover("account-panel")}>Orders</Link>
                <Link href="/api/auth/account">Profile and security</Link>
                <button type="button" className="text-button" onClick={() => void logout()}>Sign out</button>
              </div>
            </>
          ) : (
            <Link className="header-action" href={`/api/auth/login?returnTo=${encodeURIComponent(pathname)}`}><UserIcon /><span className="header-action__label">Sign in</span></Link>
          )}
          <BagButton className="header-action header-action--bag" />
        </div>
      </div>

      <div id="category-panel" popover="auto" className="header-popover header-popover--categories">
        <p className="t-meta">Shop by space</p>
        <ul>
          {roots.map((root) => (
            <li key={root.id}>
              <Link className="t-h3" href={categoryHref(root.slug)} onClick={() => hidePopover("category-panel")}>{root.name}</Link>
              <ul>
                {categories.filter((child) => child.parentId === root.id).map((child) => (
                  <li key={child.id}><Link href={categoryHref(child.slug)} onClick={() => hidePopover("category-panel")}>{child.name}</Link></li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      </div>
    </header>
  );
}

/** The bag opens the drawer for signed-in shoppers; guests are sent to sign in first because carts are account-bound. */
export function BagButton({ className }: { className: string }) {
  const pathname = usePathname();
  const session = useStorefrontSession();
  const openCart = useCartStore((state) => state.open);
  const count = useCartStore((state) => state.cart?.totalQuantity ?? 0);
  const content = <><BagIcon /><span className="header-action__label">Bag</span>{count ? <b>{count}</b> : null}</>;
  return session.authenticated
    ? <button type="button" className={className} onClick={() => void openCart()} aria-label={`Bag, ${count} items`}>{content}</button>
    : <Link className={className} href={`/api/auth/login?returnTo=${encodeURIComponent(pathname)}`} aria-label="Bag, sign in to use">{content}</Link>;
}
