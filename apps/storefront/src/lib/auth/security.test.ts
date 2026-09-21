import { describe, expect, it } from "vitest";
import { accessTokenNeedsRefresh, isAllowedPrivateRequest, isStateChangingMethod, safeReturnPath, sessionCookieSettings, validCsrfToken, validRequestOrigin } from "./security";

describe("safeReturnPath", () => {
  it("keeps local paths, queries, and fragments", () => {
    expect(safeReturnPath("/orders/123?placed=1#receipt")).toBe("/orders/123?placed=1#receipt");
  });

  it.each(["https://evil.test", "//evil.test/path", "/\\evil.test", "javascript:alert(1)", "/ok\nSet-Cookie:x"])("rejects unsafe return path %s", (value) => {
    expect(safeReturnPath(value, "/safe")).toBe("/safe");
  });
});

describe("CSRF validation", () => {
  it("requires an exact token", () => {
    expect(validCsrfToken("known-token", "known-token")).toBe(true);
    expect(validCsrfToken("known-token", "wrong-token")).toBe(false);
    expect(validCsrfToken("known-token", null)).toBe(false);
  });

  it("identifies state-changing methods", () => {
    expect(isStateChangingMethod("GET")).toBe(false);
    expect(isStateChangingMethod("POST")).toBe(true);
  });

  it("requires the configured request origin", () => {
    expect(validRequestOrigin("https://shop.example", "https://shop.example")).toBe(true);
    expect(validRequestOrigin("https://attacker.example", "https://shop.example")).toBe(false);
    expect(validRequestOrigin(null, "https://shop.example")).toBe(false);
  });
});

describe("session token expiry", () => {
  it("refreshes inside the safety window, not before it", () => {
    expect(accessTokenNeedsRefresh(130_000, 100_000)).toBe(true);
    expect(accessTokenNeedsRefresh(130_001, 100_000)).toBe(false);
  });
});

describe("session cookie security", () => {
  it("uses HttpOnly Secure SameSite cookies in production", () => {
    expect(sessionCookieSettings(true)).toEqual({ httpOnly: true, secure: true, sameSite: "lax", path: "/" });
  });
});

describe("private BFF allow-list", () => {
  it.each([
    ["GET", "/cart"],
    ["PUT", "/cart/items"],
    ["DELETE", "/cart/items/variant-1"],
    ["POST", "/checkout/confirm"],
    ["GET", "/orders"],
    ["GET", "/orders/order-1"],
  ])("allows %s %s", (method, path) => expect(isAllowedPrivateRequest(method, path)).toBe(true));

  it.each([
    ["POST", "/cart"],
    ["GET", "/checkout/confirm"],
    ["DELETE", "/orders/order-1"],
    ["GET", "/catalog/products"],
    ["GET", "/orders/order-1/lines"],
  ])("denies %s %s", (method, path) => expect(isAllowedPrivateRequest(method, path)).toBe(false));
});
