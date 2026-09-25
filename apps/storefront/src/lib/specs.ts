import type { Spec } from "./types";

/** "256" alone says nothing, so bare numbers keep their label ("Pages 256"); "Over-ear" stands on its own. */
export function specText({ label, value }: Spec) {
  return /^[\d.,\s]+$/.test(value) ? `${label} ${value}` : value;
}
