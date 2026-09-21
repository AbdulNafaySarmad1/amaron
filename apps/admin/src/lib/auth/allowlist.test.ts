import { describe, expect, it } from "vitest";
import { isAllowedOperation, isAllowedSupplyChainOperation } from "./security";

describe("operations BFF allowlist", () => {
  it.each([
    ["GET", "/dashboard"], ["GET", "/variants"], ["GET", "/pricing/sku-1"], ["POST", "/pricing/simulate"],
    ["POST", "/pricing/schedules"], ["GET", "/pricing/recommendations"], ["POST", "/pricing/rec-1/approve"],
    ["GET", "/demand"], ["POST", "/forecasts/generate"], ["GET", "/inventory"], ["POST", "/inventory/adjustments"],
    ["GET", "/replenishment"], ["POST", "/replenishment/generate"], ["POST", "/replenishment/r-1/approve"],
    ["GET", "/promotions"], ["POST", "/promotions"], ["POST", "/promotions/p-1/approve"], ["GET", "/approvals"],
    ["GET", "/audit"], ["GET", "/alerts"], ["POST", "/alerts/a-1/acknowledge"],
    ["POST", "/bulk/prices/preview"], ["POST", "/bulk/prices/apply"],
  ])("allows %s %s", (method, path) => expect(isAllowedOperation(method, path)).toBe(true));

  it.each([
    ["DELETE", "/pricing/sku-1"], ["POST", "/audit"], ["GET", "/internal/users"], ["GET", "/pricing/a/b"],
    ["POST", "/bulk/prices/apply/again"], ["PATCH", "/inventory"], ["POST", "/inventory/transfers"], ["GET", "/dashboard/extra"],
  ])("rejects %s %s", (method, path) => expect(isAllowedOperation(method, path)).toBe(false));
});

describe("supply-chain BFF allowlist", () => {
  it.each([
    ["GET", "/suppliers"], ["POST", "/suppliers"], ["POST", "/suppliers/s-1/users"],
    ["GET", "/sources"], ["POST", "/purchase-orders"], ["POST", "/purchase-orders/po-1/approve"],
    ["GET", "/rfqs"], ["POST", "/rfqs"], ["POST", "/rfqs/r-1/open"], ["POST", "/rfqs/r-1/close"], ["GET", "/rfqs/r-1/quotations"], ["POST", "/rfqs/r-1/award"],
    ["GET", "/shipments"], ["POST", "/invoices"], ["POST", "/invoices/i-1/match"],
    ["GET", "/receipts"], ["POST", "/receipts"], ["GET", "/inventory-balances"],
    ["GET", "/warehouse-tasks"], ["GET", "/cycle-counts"], ["POST", "/cycle-counts"], ["POST", "/cycle-counts/c-1/start"], ["POST", "/cycle-counts/c-1/submit"], ["POST", "/cycle-counts/c-1/reconcile"],
    ["GET", "/stock-transfers"], ["POST", "/stock-transfers"], ["POST", "/stock-transfers/t-1/dispatch"], ["POST", "/stock-transfers/t-1/receive"],
  ])("allows %s %s", (method, path) => expect(isAllowedSupplyChainOperation(method, path)).toBe(true));

  it.each([
    ["DELETE", "/suppliers/s-1"], ["GET", "/suppliers/s-1/users"], ["POST", "/receipts/r-1/reverse"],
    ["GET", "/internal"], ["PATCH", "/purchase-orders/po-1"], ["GET", "/invoices/i-1"],
  ])("rejects %s %s", (method, path) => expect(isAllowedSupplyChainOperation(method, path)).toBe(false));
});
