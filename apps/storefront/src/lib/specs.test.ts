import { describe, expect, it } from "vitest";
import { specText } from "./specs";

describe("specText", () => {
  it("keeps the label for bare numbers", () => expect(specText({ label: "Pages", value: "256" })).toBe("Pages 256"));
  it("drops the label when the value describes itself", () => expect(specText({ label: "Battery", value: "30 hours" })).toBe("30 hours"));
  it("treats grouped numbers as bare", () => expect(specText({ label: "Compartments", value: "1,200" })).toBe("Compartments 1,200"));
});
