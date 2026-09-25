"use client";

import { createContext, useContext, useEffect } from "react";
import { CartDrawer } from "@/components/cart/cart-drawer";
import { setCsrfToken } from "@/lib/api";
import type { AuthSession } from "@/lib/types";
import { useCartStore } from "@/store/cart-store";
import { useSavedStore } from "@/store/saved-store";

const SessionContext = createContext<AuthSession>({ authenticated: false });

export function useStorefrontSession() {
  return useContext(SessionContext);
}

export function StorefrontProvider({ children, session }: { children: React.ReactNode; session: AuthSession }) {
  const load = useCartStore((state) => state.load);
  const reset = useCartStore((state) => state.reset);
  // Saved items are device-local; read them after hydration so the server and first client render agree.
  useEffect(() => { void useSavedStore.persist.rehydrate(); }, []);
  useEffect(() => {
    setCsrfToken(session.authenticated ? session.csrfToken : null);
    if (session.authenticated) void load();
    else reset();
  }, [load, reset, session]);
  return <SessionContext value={session}>{children}{session.authenticated ? <CartDrawer /> : null}</SessionContext>;
}
