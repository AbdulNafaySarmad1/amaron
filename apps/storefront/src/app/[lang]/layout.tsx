import type { Metadata } from "next";
import { cookies, headers } from "next/headers";
import { notFound } from "next/navigation";
import { fontVariables } from "@/app/fonts";
import { LocaleProvider } from "@/components/providers/locale-provider";
import { StorefrontProvider } from "@/components/providers/storefront-provider";
import { BottomNav } from "@/components/shell/bottom-nav";
import { SearchMode } from "@/components/shell/search-mode";
import { SignInGate } from "@/components/shell/sign-in-gate";
import { SiteHeader } from "@/components/shell/site-header";
import { isLocale, localeDirection, locales, parseRegion, REGION_COOKIE, regionFromHint, withLocale } from "@/i18n/config";
import { getDictionary } from "@/i18n/dictionary";
import { serverGet } from "@/lib/api";
import { publicSession } from "@/lib/auth/session";
import type { AuthSession, Category } from "@/lib/types";
import "../globals.css";

export const dynamicParams = false;
export const generateStaticParams = () => locales.map((lang) => ({ lang }));

export const metadata: Metadata = {
  title: { default: "Amaron", template: "%s | Amaron" },
  description: "Considered goods for everyday rituals.",
};

export default async function LocaleLayout({ children, params }: LayoutProps<"/[lang]">) {
  const { lang } = await params;
  if (!isLocale(lang)) notFound();
  let categories: Category[] = [];
  try { categories = await serverGet<Category[]>(withLocale("/api/catalog/categories", lang), 300); } catch { /* Navigation still works through Discover. */ }
  const [dictionary, cookieStore, headerStore, storedSession] = await Promise.all([getDictionary(lang), cookies(), headers(), publicSession()]);
  // A saved region always wins. Without one, Cloudflare's country is a first-visit hint only.
  const storedRegion = parseRegion(cookieStore.get(REGION_COOKIE)?.value);
  const region = storedRegion ?? regionFromHint(headerStore.get("cf-ipcountry"));
  const session: AuthSession = storedSession ?? { authenticated: false };
  return (
    <html lang={lang} dir={localeDirection(lang)} className={fontVariables}>
      <body>
        <LocaleProvider locale={lang} dictionary={dictionary} region={region} regionDetected={!storedRegion}>
          <StorefrontProvider session={session}>
            <a className="skip-link" href="#main-content">{dictionary.nav.skip}</a>
            <SiteHeader categories={categories} />
            <div id="main-content" tabIndex={-1}>{children}</div>
            <BottomNav />
            <SearchMode categories={categories} />
            {session.authenticated ? null : <SignInGate />}
          </StorefrontProvider>
        </LocaleProvider>
      </body>
    </html>
  );
}
