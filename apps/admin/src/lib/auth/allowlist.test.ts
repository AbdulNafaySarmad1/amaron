import { describe, expect, it } from "vitest";
import { isAllowedOperation } from "./security";

describe("operations BFF allowlist", () => {
  it.each([
    ["GET", "/dashboard"], ["GET", "/variants"], ["GET", "/pricing/sku-1"], ["POST", "/pricing/simulate"],
    ["POST", "/pricing/schedules"], ["GET", "/pricing/recommendations"], ["POST", "/pricing/rec-1/approve"],
    ["GET", "/demand"], ["POST", "/forecasts/generate"], ["GET", "/inventory"], ["POST", "/inventory/adjustments"],
    ["POST", "/inventory/transfers"], ["GET", "/replenishment"], ["POST", "/replenishment/generate"], ["POST", "/replenishment/r-1/approve"],
    ["GET", "/promotions"], ["POST", "/promotions"], ["POST", "/promotions/p-1/approve"], ["GET", "/approvals"],
    ["GET", "/audit"], ["GET", "/alerts"], ["POST", "/alerts/a-1/acknowledge"],
    ["POST", "/bulk/prices/preview"], ["POST", "/bulk/prices/apply"],
  ])("allows %s %s", (method, path) => expect(isAllowedOperation(method, path)).toBe(true));

  it.each([
    ["DELETE", "/pricing/sku-1"], ["POST", "/audit"], ["GET", "/internal/users"], ["GET", "/pricing/a/b"],
    ["POST", "/bulk/prices/apply/again"], ["PATCH", "/inventory"], ["GET", "/dashboard/extra"],
  ])("rejects %s %s", (method, path) => expect(isAllowedOperation(method, path)).toBe(false));
});
