import { describe, expect, it } from "vitest";
import { accessTokenNeedsRefresh, hasAdministrationAccess, safeReturnPath, sessionCookieSettings, validCsrfToken, validRequestOrigin } from "./security";

function token(payload: object) {
  return `header.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.signature`;
}

describe("admin auth security", () => {
  it("accepts only local return paths", () => {
    expect(safeReturnPath("/admin/pricing?q=one")).toBe("/admin/pricing?q=one");
    expect(safeReturnPath("//attacker.test/path")).toBe("/admin");
    expect(safeReturnPath("/\\attacker.test")).toBe("/admin");
    expect(safeReturnPath("https://attacker.test")).toBe("/admin");
  });
  it("compares CSRF values and exact origins", () => {
    expect(validCsrfToken("secret", "secret")).toBe(true);
    expect(validCsrfToken("secret", "other")).toBe(false);
    expect(validRequestOrigin("http://localhost:3001", "http://localhost:3001/admin")).toBe(true);
    expect(validRequestOrigin("http://localhost:30010", "http://localhost:3001")).toBe(false);
  });
  it("uses secure opaque-cookie settings", () => {
    expect(sessionCookieSettings(true)).toMatchObject({ httpOnly: true, secure: true, sameSite: "lax", path: "/" });
  });
  it("refreshes before expiry", () => {
    expect(accessTokenNeedsRefresh(40_000, 0, 30_000)).toBe(false);
    expect(accessTokenNeedsRefresh(30_000, 0, 30_000)).toBe(true);
  });
  it("requires the administration role from the issued access token", () => {
    expect(hasAdministrationAccess(token({ role: ["administration-access"] }))).toBe(true);
    expect(hasAdministrationAccess(token({ resource_access: { "commerce-api": { roles: ["administration-access"] } } }))).toBe(true);
    expect(hasAdministrationAccess(token({ role: ["customer"] }))).toBe(false);
    expect(hasAdministrationAccess("not-a-token")).toBe(false);
  });
});
