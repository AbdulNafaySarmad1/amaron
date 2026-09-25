import { describe, expect, it } from "vitest";
import { locales } from "./config";
import { format, getDictionary, withFallback } from "./dictionary";
import en from "./dictionaries/en.json";

function keys(value: unknown, prefix = ""): string[] {
  return value && typeof value === "object"
    ? Object.entries(value).flatMap(([key, child]) => keys(child, prefix ? `${prefix}.${key}` : key))
    : [prefix];
}
const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

describe("dictionaries", () => {
  it.each(locales.filter((l) => l !== "en"))("%s translates every English key with the same placeholders", async (locale) => {
    const raw = (await import(`./dictionaries/${locale}.json`)).default;
    expect(keys(raw).sort()).toEqual(keys(en).sort());
    const dict = await getDictionary(locale);
    for (const key of keys(en)) {
      const english = key.split(".").reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], en) as string;
      const translated = key.split(".").reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], dict) as string;
      expect(placeholders(translated), `${locale}:${key}`).toEqual(placeholders(english));
    }
  });

  it("falls back to English for missing or mistyped entries", () => {
    const merged = withFallback({ a: { b: "B", c: "C" }, d: "D" }, { a: { b: "Б" }, d: 5 as unknown as string });
    expect(merged).toEqual({ a: { b: "Б", c: "C" }, d: "D" });
  });

  it("formats placeholders and leaves unknown ones visible", () => {
    expect(format("{count} items in {place}", { count: 3 })).toBe("3 items in {place}");
  });
});
