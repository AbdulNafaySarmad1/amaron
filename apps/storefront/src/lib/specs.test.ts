import { describe, expect, it } from "vitest";
import { specText, splitDescription } from "./specs";

describe("specText", () => {
  it("keeps the label for bare numbers", () => expect(specText({ label: "Pages", value: "256" })).toBe("Pages 256"));
  it("drops the label when the value describes itself", () => expect(specText({ label: "Battery", value: "30 hours" })).toBe("30 hours"));
  it("treats grouped numbers as bare", () => expect(specText({ label: "Compartments", value: "1,200" })).toBe("Compartments 1,200"));
});

describe("splitDescription", () => {
  it("leads with the first sentence", () => expect(splitDescription("Quiet and light. Folds flat. Lasts all day.")).toEqual({ lead: "Quiet and light.", more: "Folds flat. Lasts all day." }));
  it("has nothing more for one sentence", () => expect(splitDescription("A reliable lamp designed for everyday use.")).toEqual({ lead: "A reliable lamp designed for everyday use.", more: "" }));
});
