import { describe, expect, it } from "vitest";
import { chartPoints, csv, extractRows, percentChange, stableSort } from "./data";

describe("operational data utilities", () => {
  it("extracts records without inventing data", () => {
    expect(extractRows({ items: [{ id: "one" }] })).toEqual([{ id: "one" }]);
    expect(extractRows({ message: "empty" })).toEqual([]);
    expect(extractRows(null)).toEqual([]);
  });
  it("sorts numeric values stably", () => {
    expect(stableSort([{ id: "a", value: 2 }, { id: "b", value: 1 }, { id: "c", value: 2 }], "value", "asc").map((row) => row.id)).toEqual(["b", "a", "c"]);
  });
  it("handles safe percentage math", () => {
    expect(percentChange(120, 100)).toBe(20);
    expect(percentChange(1, 0)).toBeNull();
    expect(percentChange(Number.NaN, 2)).toBeNull();
  });
  it("handles empty, flat, and single-value chart series", () => {
    expect(chartPoints([])).toBe("");
    expect(chartPoints([4])).toBe("300.00,90.00");
    expect(chartPoints([4, 4])).toBe("12.00,90.00 588.00,90.00");
    expect(chartPoints([0, 10])).toBe("12.00,168.00 588.00,12.00");
  });
  it("escapes CSV formulas and quotes as text output", () => {
    expect(csv([{ name: "A \"quote\"" }], ["name"])).toContain('"A ""quote"""');
    expect(csv([{ name: "=SUM(A1:A2)" }], ["name"])).toContain('"\'=SUM(A1:A2)"');
  });
});
