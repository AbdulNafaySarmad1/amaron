"use client";
import { FormEvent, useRef, useState } from "react";
import { useSession } from "./session-context";

export function TransferWorkbench() {
  const { csrfToken } = useSession();
  const [fromWarehouseId, setFromWarehouseId] = useState("");
  const [toWarehouseId, setToWarehouseId] = useState("");
  const [variantId, setVariantId] = useState("");
  const [lotId, setLotId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [transferId, setTransferId] = useState("");
  const [notice, setNotice] = useState("");
  const createKey = useRef(crypto.randomUUID());
  const dispatchKey = useRef(crypto.randomUUID());
  const receiveKey = useRef(crypto.randomUUID());

  async function request(path: string, body?: object, idempotencyKey = crypto.randomUUID()) {
    const response = await fetch(`/api/admin/supply-chain${path}`, { method: "POST", headers: { "Content-Type": "application/json", "x-csrf-token": csrfToken, "Idempotency-Key": idempotencyKey }, body: JSON.stringify(body ?? {}) });
    const payload = await response.json().catch(() => null) as { id?: string; number?: string; status?: string } | null;
    if (!response.ok) { setNotice(`Transfer command failed (${response.status}).`); return null; }
    setNotice(`${payload?.number ?? "Transfer"}: ${payload?.status ?? "command completed"}.`); return payload;
  }
  async function create(event: FormEvent) {
    event.preventDefault();
    const amount = Number(quantity);
    if (!fromWarehouseId.trim() || !toWarehouseId.trim() || !variantId.trim() || !Number.isInteger(amount) || amount < 1) { setNotice("Source, destination, variant, and a positive whole quantity are required."); return; }
    const payload = await request("/stock-transfers", { fromWarehouseId: fromWarehouseId.trim(), toWarehouseId: toWarehouseId.trim(), lines: [{ variantId: variantId.trim(), lotId: lotId.trim() || null, quantity: amount }] }, createKey.current);
    if (payload?.id) { setTransferId(payload.id); createKey.current = crypto.randomUUID(); dispatchKey.current = crypto.randomUUID(); receiveKey.current = crypto.randomUUID(); }
  }
  async function transition(action: "dispatch" | "receive") { await request(`/stock-transfers/${encodeURIComponent(transferId.trim())}/${action}`, undefined, action === "dispatch" ? dispatchKey.current : receiveKey.current); }

  return <div className="panel table-panel">
    <div className="panel-heading"><div><h2>Transfer execution</h2><p>Dispatch removes sellable stock and creates in-transit balance. Receipt converts only those units back to available stock.</p></div><span className="environment">TWO-STEP POSTING</span></div>
    <form className="table-toolbar" onSubmit={create}><div className="filter"><label>Source warehouse<input value={fromWarehouseId} onChange={(event) => setFromWarehouseId(event.target.value)} /></label><label>Destination warehouse<input value={toWarehouseId} onChange={(event) => setToWarehouseId(event.target.value)} /></label><label>Variant / scan<input value={variantId} onChange={(event) => setVariantId(event.target.value)} /></label><label>Lot ID, optional<input value={lotId} onChange={(event) => setLotId(event.target.value)} /></label><label>Quantity<input type="number" min="1" value={quantity} onChange={(event) => setQuantity(event.target.value)} /></label></div><div className="toolbar-actions"><button className="secondary" type="submit">Create draft</button></div></form>
    <div className="table-toolbar"><div className="filter"><label>Transfer ID<input value={transferId} onChange={(event) => { setTransferId(event.target.value); dispatchKey.current = crypto.randomUUID(); receiveKey.current = crypto.randomUUID(); }} /></label></div><div className="toolbar-actions"><button className="danger" type="button" disabled={!transferId.trim()} onClick={() => transition("dispatch")}>Dispatch stock</button><button className="primary" type="button" disabled={!transferId.trim()} onClick={() => transition("receive")}>Receive stock</button></div></div>
    <div className="notice" aria-live="polite">{notice}</div>
  </div>;
}
