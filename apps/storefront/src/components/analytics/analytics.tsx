"use client";

import Script from "next/script";
import { useEffect } from "react";
import { dataLayerProvider, registerTelemetryProvider, track } from "@/lib/telemetry";

const GTM_ID = /^GTM-[A-Z0-9]{4,12}$/;

/** Loads Google Tag Manager only when a valid container ID is configured; otherwise tracking stays a no-op. */
export function Analytics({ gtmId }: { gtmId?: string }) {
  const enabled = !!gtmId && GTM_ID.test(gtmId);
  useEffect(() => {
    if (!enabled) return;
    const w = window as Window & { dataLayer?: Record<string, unknown>[] };
    (w.dataLayer ??= []).push({ "gtm.start": Date.now(), event: "gtm.js" });
    registerTelemetryProvider(dataLayerProvider);
  }, [enabled]);
  return enabled ? <Script id="gtm" strategy="afterInteractive" src={`https://www.googletagmanager.com/gtm.js?id=${gtmId}`} /> : null;
}

/** Reports a search that arrived by URL (results pages), once per term. */
export function TrackSearch({ term }: { term: string }) {
  useEffect(() => { track({ name: "search", search_term: term }); }, [term]);
  return null;
}
