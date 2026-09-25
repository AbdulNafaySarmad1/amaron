import type { Metadata } from "next";
import Link from "next/link";
import { fontVariables } from "@/app/fonts";
import { defaultLocale } from "@/i18n/config";
import "./globals.css";

export const metadata: Metadata = { title: "Page not found | Amaron" };

// Unmatched URLs have no locale to render in, so this page uses the default language.
export default function GlobalNotFound() {
  return (
    <html lang={defaultLocale} className={fontVariables}>
      <body>
        <main className="not-found">
          <span>404</span>
          <h1>This page wandered off</h1>
          <p>The link may be old, or the item may have moved.</p>
          <Link className="button button--primary button--medium" href="/">Back to the store</Link>
        </main>
      </body>
    </html>
  );
}
