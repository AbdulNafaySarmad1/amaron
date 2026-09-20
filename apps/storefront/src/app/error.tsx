"use client";

import { useEffect } from "react";
import { Button } from "@/components/ui/button";

export default function ErrorPage({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => { console.error(error); }, [error]);
  return <main className="not-found"><span>!</span><p className="eyebrow">A brief interruption</p><h1>We couldn&apos;t load this page.</h1><p>Looks like the connection dropped. Try again in a moment.</p><Button onClick={reset}>Try again</Button></main>;
}
