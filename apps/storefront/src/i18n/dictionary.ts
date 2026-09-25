import en from "./dictionaries/en.json";
import type { Locale } from "./config";

export type Dictionary = typeof en;

type DeepPartial<T> = { [K in keyof T]?: T[K] extends object ? DeepPartial<T[K]> : T[K] };

const loaders: Record<Locale, () => Promise<DeepPartial<Dictionary>>> = {
  en: async () => en,
  ru: () => import("./dictionaries/ru.json").then((m) => m.default),
  ur: () => import("./dictionaries/ur.json").then((m) => m.default),
  ar: () => import("./dictionaries/ar.json").then((m) => m.default),
};

/** Overlays a translation on English so a missing key renders English instead of a blank or a key name. */
export function withFallback<T extends Record<string, unknown>>(base: T, overlay: DeepPartial<T> | undefined): T {
  const result: Record<string, unknown> = { ...base };
  for (const [key, value] of Object.entries(overlay ?? {})) {
    const fallback = base[key];
    if (value === undefined) continue;
    result[key] = fallback && typeof fallback === "object" && typeof value === "object"
      ? withFallback(fallback as Record<string, unknown>, value as DeepPartial<Record<string, unknown>>)
      : typeof value === typeof fallback ? value : fallback;
  }
  return result as T;
}

export async function getDictionary(locale: Locale): Promise<Dictionary> {
  return locale === "en" ? en : withFallback(en, await loaders[locale]());
}

/** "{count} items" + { count: 3 } -> "3 items". Unknown placeholders stay visible rather than vanishing. */
export function format(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) => (key in values ? String(values[key]) : match));
}
