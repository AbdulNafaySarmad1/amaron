"use client";

import { usePathname } from "next/navigation";
import { BagIcon, ChevronIcon, HeartIcon, LockIcon, SearchIcon, UserIcon } from "@/components/icons";
import { Link, useIntlLocale, useLocale, useT } from "@/components/providers/locale-provider";
import { useStorefrontSession } from "@/components/providers/storefront-provider";
import { PreferencesControl } from "@/components/shell/preferences";
import { useRequireSignIn } from "@/components/shell/sign-in-gate";
import { format, plural } from "@/i18n/dictionary";
import { CATALOG_LANG, stripLocale } from "@/i18n/config";
import { browserRequest } from "@/lib/api";
import { categoryPath } from "@/lib/categories";
import type { Category } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";
import { useSavedStore } from "@/store/saved-store";
import { useSearchMode } from "@/store/search-store";


function hidePopover(id: string) {
  document.getElementById(id)?.hidePopover();
}

/** Path without the locale, for active-state checks: "/ur/saved" -> "/saved". */
export function useAppPath() {
  return stripLocale(usePathname());
}

/** The category whose page is open, if any. Search defaults to it so a shopper never silently leaves the space. */
export function useCategoryScope(categories: Category[]) {
  const [, section, slug] = useAppPath().split("/");
  return section === "c" ? categories.find((category) => category.slug === slug) : undefined;
}

export function SiteHeader({ categories }: { categories: Category[] }) {
  const t = useT();
  const intl = useIntlLocale();
  const locale = useLocale();
  const path = useAppPath();
  const pathname = usePathname();
  const session = useStorefrontSession();
  const openSearch = useSearchMode((state) => state.open);
  const savedCount = useSavedStore((state) => state.ids.length);
  const roots = categories.filter((category) => !category.parentId);
  const scope = useCategoryScope(categories);

  async function logout() {
    const result = await browserRequest<{ logoutUrl: string }>(`/api/auth/logout?locale=${locale}`, { method: "POST" });
    window.location.assign(result.logoutUrl);
  }

  // Checkout stops selling: no search, categories or saved items competing with the order.
  if (path.startsWith("/checkout")) {
    return (
      <header className="site-header site-header--checkout">
        <div className="site-header__inner">
          <Link className="wordmark" href="/" aria-label="Amaron"><span>A</span>maron</Link>
          <p className="t-meta checkout-badge"><LockIcon />{t.checkout.secure}</p>
          <Link className="text-button" href="/">{t.checkout.backToStore}</Link>
        </div>
      </header>
    );
  }

  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link className="wordmark" href="/" aria-label="Amaron"><span>A</span>maron</Link>

        <nav className="primary-nav" aria-label={t.nav.primary}>
          <Link href="/search" aria-current={path === "/search" ? "page" : undefined}>{t.nav.discover}</Link>
          <button type="button" className="primary-nav__menu" popoverTarget="category-panel">{t.nav.categories} <ChevronIcon width={16} height={16} /></button>
        </nav>

        <button type="button" className="search-trigger" onClick={openSearch} aria-haspopup="dialog">
          <SearchIcon />
          <span>{scope ? format(t.category.searchIn, { category: scope.name }) : t.search.placeholder}</span>
          <kbd aria-hidden="true">/</kbd>
        </button>

        <div className="header-actions">
          <button type="button" className="header-action header-action--search" onClick={openSearch} aria-label={t.search.trigger}><SearchIcon /></button>
          <PreferencesControl />
          <Link className="header-action header-action--saved" href="/saved" aria-current={path === "/saved" ? "page" : undefined} aria-label={plural(t.nav.savedCount, savedCount, intl)}>
            <HeartIcon /><span className="header-action__label">{t.nav.saved}</span>{savedCount ? <b>{savedCount}</b> : null}
          </Link>
          {session.authenticated ? (
            <>
              <button type="button" className="header-action" popoverTarget="account-panel" aria-label={t.nav.account}><UserIcon /><span className="header-action__label">{t.nav.account}</span></button>
              <div id="account-panel" popover="auto" className="header-popover header-popover--account">
                <p className="t-meta">{t.nav.signedInAs}</p>
                <p className="t-ui">{session.user.name ?? t.nav.yourAccount}</p>
                <Link href="/orders" onClick={() => hidePopover("account-panel")}>{t.nav.orders}</Link>
                <Link href="/api/auth/account" prefetch={false}>{t.nav.profile}</Link>
                <button type="button" className="text-button" onClick={() => void logout()}>{t.nav.signOut}</button>
              </div>
            </>
          ) : (
            <a className="header-action" href={`/api/auth/login?returnTo=${encodeURIComponent(pathname)}`}><UserIcon /><span className="header-action__label">{t.nav.signIn}</span></a>
          )}
          <BagButton className="header-action header-action--bag" />
        </div>
      </div>

      <div id="category-panel" popover="auto" className="header-popover header-popover--categories">
        <p className="t-meta">{t.nav.shopBySpace}</p>
        <ul>
          {roots.map((root) => (
            <li key={root.id}>
              <Link className="t-h3" href={categoryPath(root.slug)} onClick={() => hidePopover("category-panel")} lang={CATALOG_LANG} dir="auto">{root.name}</Link>
              <ul>
                {categories.filter((child) => child.parentId === root.id).map((child) => (
                  <li key={child.id}><Link href={categoryPath(child.slug)} onClick={() => hidePopover("category-panel")} lang={CATALOG_LANG} dir="auto">{child.name}</Link></li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      </div>
    </header>
  );
}

/** The bag opens the drawer for signed-in shoppers; guests are told why it needs an account, and stay put. */
export function BagButton({ className }: { className: string }) {
  const t = useT();
  const intl = useIntlLocale();
  const session = useStorefrontSession();
  const requireSignIn = useRequireSignIn();
  const openCart = useCartStore((state) => state.open);
  const count = useCartStore((state) => state.cart?.totalQuantity ?? 0);
  const content = <><BagIcon /><span className="header-action__label">{t.nav.bag}</span>{count ? <b>{count}</b> : null}</>;
  return session.authenticated
    ? <button type="button" className={className} onClick={() => void openCart()} aria-label={plural(t.nav.bagCount, count, intl)}>{content}</button>
    : <button type="button" className={className} onClick={() => requireSignIn("bag")} aria-haspopup="dialog" aria-label={t.nav.bagSignIn}>{content}</button>;
}
