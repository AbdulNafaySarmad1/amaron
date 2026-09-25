import { describe, expect, it } from "vitest";
import { locales } from "./config";
import { format, getDictionary, plural, withFallback } from "./dictionary";
import en from "./dictionaries/en.json";

const PLURAL = new Set(["zero", "one", "two", "few", "many", "other"]);
const isPlural = (value: unknown) => !!value && typeof value === "object" && "other" in value && Object.keys(value).every((k) => PLURAL.has(k));
// A plural entry is one key: languages legitimately use different sets of forms.
function keys(value: unknown, prefix = ""): string[] {
  return value && typeof value === "object" && !isPlural(value)
    ? Object.entries(value).flatMap(([key, child]) => keys(child, prefix ? `${prefix}.${key}` : key))
    : [prefix];
}
const text = (value: unknown) => (isPlural(value) ? Object.values(value as object).join(" ") : String(value));
const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

describe("dictionaries", () => {
  it.each(locales.filter((l) => l !== "en"))("%s translates every English key with the same placeholders", async (locale) => {
    const raw = (await import(`./dictionaries/${locale}.json`)).default;
    expect(keys(raw).sort()).toEqual(keys(en).sort());
    const dict = await getDictionary(locale);
    for (const key of keys(en)) {
      const english = key.split(".").reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], en);
      const translated = key.split(".").reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], dict);
      // Every placeholder a translation uses must exist in English (a plural form may omit {count}, e.g. Arabic "one").
      for (const name of placeholders(text(translated))) expect(placeholders(text(english)), `${locale}:${key}`).toContain(name);
      if (!isPlural(english)) expect(placeholders(text(translated)), `${locale}:${key}`).toEqual(placeholders(text(english)));
    }
  });

  it("falls back to English for missing or mistyped entries", () => {
    const merged = withFallback({ a: { b: "B", c: "C" }, d: "D" }, { a: { b: "Б" }, d: 5 as unknown as string });
    expect(merged).toEqual({ a: { b: "Б", c: "C" }, d: "D" });
  });

  it("formats placeholders and leaves unknown ones visible", () => {
    expect(format("{count} items in {place}", { count: 3 })).toBe("3 items in {place}");
  });

  it("chooses plural forms by each language's rules", async () => {
    const [ru, ar, en] = await Promise.all([getDictionary("ru"), getDictionary("ar"), getDictionary("en")]);
    expect(plural(en.cart.items, 1, "en-US")).toBe("1 item");
    expect(plural(en.cart.items, 3, "en-US")).toBe("3 items");
    expect([1, 2, 5, 21].map((n) => plural(ru.cart.items, n, "ru-RU"))).toEqual(["1 товар", "2 товара", "5 товаров", "21 товар"]);
    expect([0, 1, 2, 3, 11, 100].map((n) => plural(ar.cart.items, n, "ar"))).toEqual(["لا عناصر", "عنصر واحد", "عنصران", "3 عناصر", "11 عنصرًا", "100 عنصر"]);
  });
});
