"use client";

import { useCallback, useEffect, useRef } from "react";
import { create } from "zustand";
import { CloseIcon } from "@/components/icons";
import { useT } from "@/components/providers/locale-provider";
import { useStorefrontSession } from "@/components/providers/storefront-provider";

// A route handler that redirects to the identity provider, so it needs a full navigation, not a client transition.
const LOGIN_PATH = "/api/auth/login";

/** Actions that need an account. Browsing, searching and saving never do. */
export type GatedAction = "bag" | "review";

const useGate = create<{ action: GatedAction | null; open: (action: GatedAction) => void; close: () => void }>((set) => ({
  action: null,
  open: (action) => set({ action }),
  close: () => set({ action: null }),
}));

/** Returns a check to run before an account-only action: true when signed in, otherwise explains and offers sign-in. */
export function useRequireSignIn() {
  const session = useStorefrontSession();
  const open = useGate((state) => state.open);
  return useCallback((action: GatedAction) => {
    if (session.authenticated) return true;
    open(action);
    return false;
  }, [session.authenticated, open]);
}

/** A calm interruption, not a redirect: the guest keeps their place and chooses whether to sign in now. */
export function SignInGate() {
  const t = useT();
  const { action, close } = useGate();
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const element = dialog.current;
    if (!element) return;
    if (action && !element.open) element.showModal();
    if (!action && element.open) element.close();
  }, [action]);

  const copy = action === "review" ? { title: t.gate.reviewTitle, body: t.gate.reviewBody } : { title: t.gate.bagTitle, body: t.gate.bagBody };

  return (
    <dialog ref={dialog} className="sign-in-gate" aria-labelledby="sign-in-gate-title" onClose={close} onClick={(event) => { if (event.target === event.currentTarget) close(); }}>
      <div className="sign-in-gate__panel">
        <button type="button" className="icon-button sign-in-gate__close" onClick={close} aria-label={t.gate.keepBrowsing}><CloseIcon /></button>
        <h2 id="sign-in-gate-title" className="t-h3">{copy.title}</h2>
        <p className="t-body">{copy.body}</p>
        <div className="sign-in-gate__actions">
          {/* The return path is read at click time so it keeps the query string (filters, search terms). */}
          <a className="button button--primary button--medium" href={LOGIN_PATH} autoFocus onClick={(event) => { event.currentTarget.href = `${LOGIN_PATH}?returnTo=${encodeURIComponent(window.location.pathname + window.location.search)}`; }}>{t.gate.signIn}</a>
          <button type="button" className="button button--secondary button--medium" onClick={close}>{t.gate.keepBrowsing}</button>
        </div>
      </div>
    </dialog>
  );
}
