"use client";
import { FormEvent, useMemo, useState } from "react";
import { useSession } from "./session-context";

type Fields = {
  purchaseOrderId: string; inboundShipmentId: string; warehouseId: string; purchaseOrderLineId: string;
  expected: string; accepted: string; damaged: string; quarantined: string; lotNumber: string; expiresAt: string;
};

const initial: Fields = { purchaseOrderId: "", inboundShipmentId: "", warehouseId: "", purchaseOrderLineId: "", expected: "", accepted: "", damaged: "0", quarantined: "0", lotNumber: "", expiresAt: "" };

export function ReceivingWorkbench() {
  const { csrfToken } = useSession();
  const [fields, setFields] = useState(initial);
  const [notice, setNotice] = useState("");
  const [posting, setPosting] = useState(false);
  const quantities = useMemo(() => {
    const expected = Number(fields.expected) || 0;
    const accepted = Number(fields.accepted) || 0;
    const damaged = Number(fields.damaged) || 0;
    const quarantined = Number(fields.quarantined) || 0;
    const physical = accepted + damaged + quarantined;
    return { expected, accepted, damaged, quarantined, physical, missing: Math.max(0, expected - physical) };
  }, [fields]);

  function change(key: keyof Fields, value: string) { setFields((current) => ({ ...current, [key]: value })); }
  async function submit(event: FormEvent) {
    event.preventDefault(); setNotice("");
    if ([fields.purchaseOrderId, fields.warehouseId, fields.purchaseOrderLineId].some((value) => !value.trim()) || quantities.expected < 1 || quantities.physical < 0) { setNotice("Purchase order, warehouse, line, and valid quantities are required."); return; }
    setPosting(true);
    const response = await fetch("/api/admin/supply-chain/receipts", {
      method: "POST",
      headers: { "Content-Type": "application/json", "x-csrf-token": csrfToken, "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify({
        purchaseOrderId: fields.purchaseOrderId.trim(), inboundShipmentId: fields.inboundShipmentId.trim() || null, warehouseId: fields.warehouseId.trim(),
        lines: [{ purchaseOrderLineId: fields.purchaseOrderLineId.trim(), expectedQuantity: quantities.expected, physicalQuantity: quantities.physical, acceptedQuantity: quantities.accepted, damagedQuantity: quantities.damaged, quarantinedQuantity: quantities.quarantined, lotNumber: fields.lotNumber.trim() || null, expiresAt: fields.expiresAt ? new Date(fields.expiresAt).toISOString() : null }],
      }),
    });
    setPosting(false);
    if (!response.ok) { setNotice(`Receipt was not posted (${response.status}). Verify identifiers, state, and remaining quantities.`); return; }
    const result = await response.json() as { number?: string };
    setNotice(`Receipt ${result.number ?? "posted"}: +${quantities.accepted} available, +${quantities.damaged} damaged, +${quantities.quarantined} quarantined, ${quantities.missing} missing.`);
    setFields(initial);
  }

  return <div className="panel table-panel">
    <div className="panel-heading"><div><h2>Post physical receipt</h2><p>Scan or enter immutable identifiers. Expected quantities never become stock until this command succeeds.</p></div><span className="environment">PHYSICAL AUTHORITY</span></div>
    <form className="table-toolbar" onSubmit={submit}>
      <div className="filter"><label>Purchase order ID<input autoFocus value={fields.purchaseOrderId} onChange={(event) => change("purchaseOrderId", event.target.value)} /></label><label>ASN ID, optional<input value={fields.inboundShipmentId} onChange={(event) => change("inboundShipmentId", event.target.value)} /></label><label>Warehouse ID<input value={fields.warehouseId} onChange={(event) => change("warehouseId", event.target.value)} /></label><label>PO line / scan<input value={fields.purchaseOrderLineId} onChange={(event) => change("purchaseOrderLineId", event.target.value)} /></label></div>
      <div className="toolbar-actions"><label>Expected<input type="number" min="1" value={fields.expected} onChange={(event) => change("expected", event.target.value)} /></label><label>Accepted<input type="number" min="0" value={fields.accepted} onChange={(event) => change("accepted", event.target.value)} /></label><label>Damaged<input type="number" min="0" value={fields.damaged} onChange={(event) => change("damaged", event.target.value)} /></label><label>Quarantined<input type="number" min="0" value={fields.quarantined} onChange={(event) => change("quarantined", event.target.value)} /></label><label>Lot<input value={fields.lotNumber} onChange={(event) => change("lotNumber", event.target.value)} /></label><label>Expiry<input type="date" value={fields.expiresAt} onChange={(event) => change("expiresAt", event.target.value)} /></label><button className="primary" disabled={posting}>{posting ? "Posting…" : "Post receipt"}</button></div>
    </form>
    <div className="notice" aria-live="polite">Physical {quantities.physical} / expected {quantities.expected}; missing {quantities.missing}. {notice}</div>
  </div>;
}
