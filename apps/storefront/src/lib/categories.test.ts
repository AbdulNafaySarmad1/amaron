import { describe, expect, it } from "vitest";
import { categoryTrail, childCategories } from "./categories";

const tree = [
  { id: "e", slug: "electronics", name: "Electronics", parentId: null, locale: "en" },
  { id: "a", slug: "audio", name: "Audio", parentId: "e", locale: "en" },
  { id: "h", slug: "headphones", name: "Headphones", parentId: "a", locale: "en" },
  { id: "b", slug: "books", name: "Books", parentId: null, locale: "en" },
];

describe("categories", () => {
  it("builds a root-first trail", () => expect(categoryTrail(tree, "headphones").map((c) => c.slug)).toEqual(["electronics", "audio", "headphones"]));
  it("returns nothing for unknown slugs", () => expect(categoryTrail(tree, "nope")).toEqual([]));
  it("stops on cyclic parents", () => expect(categoryTrail([{ id: "x", slug: "x", name: "X", parentId: "y", locale: "en" }, { id: "y", slug: "y", name: "Y", parentId: "x", locale: "en" }], "x").length).toBe(2));
  it("lists direct children only", () => expect(childCategories(tree, "e").map((c) => c.slug)).toEqual(["audio"]));
});
