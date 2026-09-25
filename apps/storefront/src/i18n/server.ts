import { lang } from "next/root-params";
import { redirect } from "next/navigation";
import { defaultLocale, isLocale, type Locale, localizePath } from "./config";
import { getDictionary } from "./dictionary";

/** The locale of the current request, readable from any Server Component. */
export async function currentLocale(): Promise<Locale> {
  const value = await lang();
  return isLocale(value) ? value : defaultLocale;
}

export async function currentDictionary() {
  return getDictionary(await currentLocale());
}

/** Sends a signed-out shopper to login and back to the same page in the same language. */
export async function redirectToLogin(path: string): Promise<never> {
  const returnTo = localizePath(await currentLocale(), path);
  redirect(`/api/auth/login?returnTo=${encodeURIComponent(returnTo)}`);
}
