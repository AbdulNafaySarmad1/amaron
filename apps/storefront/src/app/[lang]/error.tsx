"use client";

import { useEffect } from "react";
import { useLocale } from "@/components/providers/locale-provider";
import { Button } from "@/components/ui/button";

export default function ErrorPage({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  const locale = useLocale();
  useEffect(() => { console.error(error); }, [error]);
  // Not yet translated: mark it English so it is laid out left-to-right on Urdu and Arabic pages.
  return <main className="not-found" {...(locale === "en" ? {} : { lang: "en", dir: "ltr" })}><span>!</span><p className="eyebrow">A brief interruption</p><h1>We couldn&apos;t load this page.</h1><p>Looks like the connection dropped. Try again in a moment.</p><Button onClick={reset}>Try again</Button></main>;
}
