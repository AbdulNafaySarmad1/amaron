import { describe, expect, it } from "vitest";
import { parseProductIds } from "./product-ids";

const a = "00000000-0000-0000-0000-000000000100";
const b = "00000000-0000-0000-0000-000000000101";

describe("parseProductIds", () => {
  it("accepts unique UUIDs", () => expect(parseProductIds(`${a},${b}`)).toEqual([a, b]));
  it.each([
    ["missing", null],
    ["empty", ""],
    ["too many", Array.from({ length: 51 }, (_, i) => `00000000-0000-0000-0000-${String(i).padStart(12, "0")}`).join(",")],
    ["not a UUID", "'; drop table products;--"],
    ["trailing comma", `${a},`],
    ["duplicate ignoring case", `${a},${a.toUpperCase()}`],
  ])("rejects %s", (_, raw) => expect(parseProductIds(raw)).toBeNull());
});
