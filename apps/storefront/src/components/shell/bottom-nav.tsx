"use client";

import { CompassIcon, HeartIcon, HomeIcon } from "@/components/icons";
import { Link, useT } from "@/components/providers/locale-provider";
import { BagButton, useAppPath } from "@/components/shell/site-header";
import { useSavedStore } from "@/store/saved-store";
import { useSearchMode } from "@/store/search-store";

/** Mobile navigation: four destinations, always visible, no hidden menu. Hidden from checkout so it cannot distract. */
export function BottomNav() {
  const t = useT();
  const path = useAppPath();
  const openSearch = useSearchMode((state) => state.open);
  const savedCount = useSavedStore((state) => state.ids.length);
  if (path.startsWith("/checkout")) return null;
  return (
    <nav className="bottom-nav" aria-label={t.nav.primary}>
      <Link href="/" aria-current={path === "/" ? "page" : undefined}><HomeIcon /><span>{t.nav.home}</span></Link>
      <button type="button" onClick={openSearch} aria-haspopup="dialog"><CompassIcon /><span>{t.nav.explore}</span></button>
      <Link href="/saved" aria-current={path === "/saved" ? "page" : undefined}><HeartIcon /><span>{t.nav.saved}</span>{savedCount ? <b>{savedCount}</b> : null}</Link>
      <BagButton className="bottom-nav__bag" />
    </nav>
  );
}
