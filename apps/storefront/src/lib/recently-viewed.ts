const KEY = "amaron.recently-viewed";
const LIMIT = 12;

/** Device-local product history for "Recently explored". Storage failures just mean an empty history. */
export function readRecentlyViewed(): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(KEY) ?? "[]");
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === "string").slice(0, LIMIT) : [];
  } catch { return []; }
}

export function recordViewed(productId: string) {
  const next = [productId, ...readRecentlyViewed().filter((id) => id !== productId)].slice(0, LIMIT);
  try { localStorage.setItem(KEY, JSON.stringify(next)); } catch { /* device storage unavailable */ }
}
