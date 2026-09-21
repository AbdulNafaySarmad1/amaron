"use client";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { SessionProvider } from "./session-context";

const navigation = [
  ["Overview", "/admin"], ["Pricing", "/admin/pricing"], ["Recommendations", "/admin/pricing/recommendations"],
  ["Demand", "/admin/demand"], ["Inventory", "/admin/inventory"], ["Inventory forecast", "/admin/inventory/forecast"],
  ["Replenishment", "/admin/replenishment"], ["Promotions", "/admin/promotions"], ["Approvals", "/admin/approvals"], ["Audit", "/admin/audit"],
] as const;

export function AppShell({ children, userName, csrfToken }: { children: React.ReactNode; userName: string; csrfToken: string }) {
  const pathname = usePathname();
  const router = useRouter();
  const [menu, setMenu] = useState(false);
  const [palette, setPalette] = useState(false);
  const [query, setQuery] = useState("");
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") { event.preventDefault(); setPalette((open) => !open); }
      if (event.key === "Escape") setPalette(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);
  async function logout() {
    const response = await fetch("/api/auth/logout", { method: "POST", headers: { "x-csrf-token": csrfToken } });
    if (response.ok) window.location.assign((await response.json() as { logoutUrl: string }).logoutUrl);
  }
  const matches = navigation.filter(([label]) => label.toLowerCase().includes(query.toLowerCase()));
  return <SessionProvider value={{ csrfToken }}>
    <div className="shell">
      <aside className={menu ? "sidebar open" : "sidebar"}>
        <div className="brand"><span className="brand-mark">A</span><span><strong>AMARON</strong><small>Operations control</small></span></div>
        <nav aria-label="Primary navigation">
          {navigation.map(([label, href]) => <Link key={href} href={href} onClick={() => setMenu(false)} className={pathname === href ? "active" : ""}>{label}</Link>)}
        </nav>
        <div className="operator"><span className="status-dot" /> <span><small>Signed in as</small><strong>{userName}</strong></span></div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <button className="icon-button mobile-only" onClick={() => setMenu(!menu)} aria-label="Toggle navigation">☰</button>
          <button className="command-trigger" onClick={() => setPalette(true)}>Search commands <kbd>Ctrl K</kbd></button>
          <div className="top-actions"><span className="environment">LIVE OPERATIONS</span><button className="text-button" onClick={logout}>Sign out</button></div>
        </header>
        <main>{children}</main>
      </div>
    </div>
    {palette && <div className="dialog-backdrop" role="presentation" onMouseDown={() => setPalette(false)}>
      <div className="command-dialog" role="dialog" aria-modal="true" aria-label="Command palette" onMouseDown={(event) => event.stopPropagation()}>
        <input autoFocus value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Go to a workspace…" aria-label="Search commands" />
        <div className="command-results">{matches.map(([label, href]) => <button key={href} onClick={() => { setPalette(false); router.push(href); }}>{label}<span>{href}</span></button>)}</div>
      </div>
    </div>}
  </SessionProvider>;
}
