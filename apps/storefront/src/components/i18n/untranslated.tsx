import { currentLocale } from "@/i18n/server";

/**
 * Marks content that has no translation yet as English, left-to-right. Without it, English copy on an Urdu or
 * Arabic page inherits dir="rtl" (sentence punctuation jumps to the wrong end) and Urdu typography, and screen
 * readers pronounce it as Urdu. Remove the wrapper from a page once its copy is in the dictionaries.
 */
export async function Untranslated({ children }: { children: React.ReactNode }) {
  return (await currentLocale()) === "en" ? children : <div lang="en" dir="ltr" className="untranslated">{children}</div>;
}
