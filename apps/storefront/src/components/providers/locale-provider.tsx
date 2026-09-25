"use client";

import NextLink from "next/link";
import { useRouter } from "next/navigation";
import { type ComponentProps, createContext, useContext, useMemo } from "react";
import { intlLocale, localizePath, type Locale, type Region } from "@/i18n/config";
import type { Dictionary } from "@/i18n/dictionary";

type LocaleContextValue = { locale: Locale; dictionary: Dictionary; region: Region; regionDetected: boolean };
const LocaleContext = createContext<LocaleContextValue | null>(null);

export function LocaleProvider({ children, ...value }: LocaleContextValue & { children: React.ReactNode }) {
  return <LocaleContext value={value}>{children}</LocaleContext>;
}

export function useLocaleContext() {
  const value = useContext(LocaleContext);
  if (!value) throw new Error("useLocaleContext must be used inside LocaleProvider.");
  return value;
}

export const useLocale = () => useLocaleContext().locale;
export const useT = () => useLocaleContext().dictionary;
export const useIntlLocale = () => intlLocale[useLocale()];

/** next/link that keeps the shopper in their language: "/search" becomes "/ur/search". */
export function Link({ href, ...props }: ComponentProps<typeof NextLink>) {
  const locale = useLocale();
  return <NextLink href={typeof href === "string" ? localizePath(locale, href) : href} {...props} />;
}

/** Router whose push/replace/prefetch localize app paths the same way Link does. */
export function useLocalizedRouter() {
  const router = useRouter();
  const locale = useLocale();
  return useMemo(() => ({
    ...router,
    push: (href: string, options?: Parameters<typeof router.push>[1]) => router.push(localizePath(locale, href), options),
    replace: (href: string, options?: Parameters<typeof router.replace>[1]) => router.replace(localizePath(locale, href), options),
    prefetch: (href: string) => router.prefetch(localizePath(locale, href)),
  }), [router, locale]);
}
