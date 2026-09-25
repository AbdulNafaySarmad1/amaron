import type { Category } from "./types";

export const categoryPath = (slug: string) => `/c/${encodeURIComponent(slug)}`;

/** Root-first chain ending at the category, for breadcrumbs. Stops on cycles or missing parents. */
export function categoryTrail(categories: Category[], slug: string): Category[] {
  const byId = new Map(categories.map((c) => [c.id, c]));
  const trail: Category[] = [];
  const seen = new Set<string>();
  for (let current = categories.find((c) => c.slug === slug); current && !seen.has(current.id); current = current.parentId ? byId.get(current.parentId) : undefined) {
    seen.add(current.id);
    trail.unshift(current);
  }
  return trail;
}

export const childCategories = (categories: Category[], id: string) => categories.filter((c) => c.parentId === id);
