const KEY = "amaron.recent-searches";
const LIMIT = 6;

export function readRecentSearches(): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(KEY) ?? "[]");
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === "string").slice(0, LIMIT) : [];
  } catch { return []; }
}

export function rememberSearch(term: string, current = readRecentSearches()): string[] {
  const next = [term, ...current.filter((x) => x.toLowerCase() !== term.toLowerCase())].slice(0, LIMIT);
  try { localStorage.setItem(KEY, JSON.stringify(next)); } catch { /* device storage unavailable */ }
  return next;
}

export function clearRecentSearches() {
  try { localStorage.removeItem(KEY); } catch { /* device storage unavailable */ }
}
