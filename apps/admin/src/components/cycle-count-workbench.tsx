"use client";
import { FormEvent, useState } from "react";
import { useSession } from "./session-context";

export function CycleCountWorkbench() {
  const { csrfToken } = useSession();
  const [warehouseId, setWarehouseId] = useState("");
  const [variantId, setVariantId] = useState("");
  const [lotId, setLotId] = useState("");
  const [state, setState] = useState("Available");
  const [countId, setCountId] = useState("");
  const [lineId, setLineId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [notice, setNotice] = useState("");

  async function post(path: string, body: object = {}) {
    const response = await fetch(`/api/admin/supply-chain${path}`, { method: "POST", headers: { "Content-Type": "application/json", "x-csrf-token": csrfToken }, body: JSON.stringify(body) });
    const payload = await response.json().catch(() => null) as { id?: string; number?: string; status?: string; lines?: Array<{ id: string; expectedQuantity: number | null; countedQuantity: number | null; difference: number | null }> } | null;
    if (!response.ok) { setNotice(`Command failed (${response.status}). No client-side assumptions were applied.`); return null; }
    setNotice(`${payload?.number ?? "Cycle count"}: ${payload?.status ?? "command completed"}.`); return payload;
  }
  async function create(event: FormEvent) {
    event.preventDefault();
    if (!warehouseId.trim() || !variantId.trim()) { setNotice("Warehouse and variant are required."); return; }
    const payload = await post("/cycle-counts", { warehouseId: warehouseId.trim(), lines: [{ variantId: variantId.trim(), locationId: null, lotId: lotId.trim() || null, state }] });
    if (payload?.id && payload.lines?.[0]) { setCountId(payload.id); setLineId(payload.lines[0].id); }
  }
  async function start() { await post(`/cycle-counts/${encodeURIComponent(countId.trim())}/start`); }
  async function submit() {
    const counted = Number(quantity);
    if (!lineId.trim() || !Number.isInteger(counted) || counted < 0) { setNotice("A count line and nonnegative whole quantity are required."); return; }
    const payload = await post(`/cycle-counts/${encodeURIComponent(countId.trim())}/submit`, { lines: [{ lineId: lineId.trim(), countedQuantity: counted }] });
    const line = payload?.lines?.[0];
    if (line && line.expectedQuantity !== null && line.difference !== null) setNotice(`Submitted. Expected ${line.expectedQuantity}; counted ${line.countedQuantity}; difference ${line.difference}. A different operator must reconcile.`);
  }
  async function reconcile() { await post(`/cycle-counts/${encodeURIComponent(countId.trim())}/reconcile`); }

  return <div className="panel table-panel">
    <div className="panel-heading"><div><h2>Blind count workbench</h2><p>Expected stock remains hidden until the count is submitted. Reconciliation requires another operator.</p></div><span className="environment">BLIND COUNT</span></div>
    <form className="table-toolbar" onSubmit={create}><div className="filter"><label>Warehouse ID<input value={warehouseId} onChange={(event) => setWarehouseId(event.target.value)} /></label><label>Variant / scan<input value={variantId} onChange={(event) => setVariantId(event.target.value)} /></label><label>Lot ID, optional<input value={lotId} onChange={(event) => setLotId(event.target.value)} /></label><label>State<select value={state} onChange={(event) => setState(event.target.value)}><option>Available</option><option>Damaged</option><option>Quarantined</option><option>Expired</option><option>PickFace</option><option>Reserve</option></select></label></div><div className="toolbar-actions"><button className="secondary" type="submit">Plan count</button></div></form>
    <div className="table-toolbar"><div className="filter"><label>Count ID<input value={countId} onChange={(event) => setCountId(event.target.value)} /></label><label>Count line ID<input value={lineId} onChange={(event) => setLineId(event.target.value)} /></label><label>Physical quantity<input type="number" min="0" value={quantity} onChange={(event) => setQuantity(event.target.value)} /></label></div><div className="toolbar-actions"><button className="secondary" type="button" disabled={!countId.trim()} onClick={start}>Start blind count</button><button className="primary" type="button" disabled={!countId.trim()} onClick={submit}>Submit count</button><button className="danger" type="button" disabled={!countId.trim()} onClick={reconcile}>Reconcile independently</button></div></div>
    <div className="notice" aria-live="polite">{notice}</div>
  </div>;
}
