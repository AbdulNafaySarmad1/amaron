const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Validates an untrusted comma-separated ID list: 1–50 unique UUIDs, else null. */
export function parseProductIds(raw: string | null): string[] | null {
  const ids = raw?.split(",") ?? [];
  if (ids.length < 1 || ids.length > 50 || !ids.every((id) => uuid.test(id))) return null;
  return new Set(ids.map((id) => id.toLowerCase())).size === ids.length ? ids : null;
}
