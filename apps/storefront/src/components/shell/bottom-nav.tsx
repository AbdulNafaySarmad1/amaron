"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { CompassIcon, HeartIcon, HomeIcon } from "@/components/icons";
import { BagButton } from "@/components/shell/site-header";
import { useSavedStore } from "@/store/saved-store";
import { useSearchMode } from "@/store/search-store";

/** Mobile navigation: four destinations, always visible, no hidden menu. Hidden from checkout so it cannot distract. */
export function BottomNav() {
  const pathname = usePathname();
  const openSearch = useSearchMode((state) => state.open);
  const savedCount = useSavedStore((state) => state.ids.length);
  if (pathname.startsWith("/checkout")) return null;
  return (
    <nav className="bottom-nav" aria-label="Primary">
      <Link href="/" aria-current={pathname === "/" ? "page" : undefined}><HomeIcon /><span>Home</span></Link>
      <button type="button" onClick={openSearch} aria-haspopup="dialog"><CompassIcon /><span>Explore</span></button>
      <Link href="/saved" aria-current={pathname === "/saved" ? "page" : undefined}><HeartIcon /><span>Saved</span>{savedCount ? <b>{savedCount}</b> : null}</Link>
      <BagButton className="bottom-nav__bag" />
    </nav>
  );
}
