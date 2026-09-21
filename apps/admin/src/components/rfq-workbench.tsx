"use client";
import { FormEvent, useState } from "react";
import { useSession } from "./session-context";

type Quote = {
  id: string; number: string; supplierOrganizationId: string; status: string;
  total: { amount: number; currency: string };
  lines: Array<{ supplierSku: string; requestedQuantity: number; unitCost: { amount: number; currency: string }; leadTimeDays: number; minimumOrderQuantity: number; orderMultiple: number; reliabilityPercent: number | null }>;
};

export function RfqWorkbench() {
  const { csrfToken } = useSession();
  const [rfqId, setRfqId] = useState("");
  const [quotes, setQuotes] = useState<Quote[]>([]);
  const [selected, setSelected] = useState("");
  const [reason, setReason] = useState("");
  const [notice, setNotice] = useState("");

  async function reload() {
    setNotice(""); setSelected("");
    const response = await fetch(`/api/admin/supply-chain/rfqs/${encodeURIComponent(rfqId.trim())}/quotations`, { cache: "no-store" });
    if (!response.ok) { setNotice(`Quotations could not be loaded (${response.status}).`); setQuotes([]); return; }
    const payload = await response.json() as Quote[];
    setQuotes(payload); setNotice(payload.length ? `${payload.length} quotation${payload.length === 1 ? "" : "s"} loaded. Compare every term before award.` : "No quotations have been submitted.");
  }
  async function load(event: FormEvent) { event.preventDefault(); await reload(); }

  async function award() {
    if (!selected || reason.trim().length < 3) { setNotice("Select one quotation and record a decision reason."); return; }
    const response = await fetch(`/api/admin/supply-chain/rfqs/${encodeURIComponent(rfqId.trim())}/award`, { method: "POST", headers: { "Content-Type": "application/json", "x-csrf-token": csrfToken }, body: JSON.stringify({ quotationId: selected, reason: reason.trim() }) });
    if (!response.ok) { setNotice(`Award failed (${response.status}). No award was recorded.`); return; }
    const result = await response.json() as { draftPurchaseOrder?: { number?: string } };
    setNotice(`Award recorded. Draft purchase order ${result.draftPurchaseOrder?.number ?? "created"} still requires purchase-order approval.`);
    await reload();
  }

  return <div className="panel table-panel">
    <div className="panel-heading"><div><h2>Quotation comparison</h2><p>Cost is one term. Compare lead time, MOQ, and order multiples, then record the human decision.</p></div><span className="environment">NO AUTO-AWARD</span></div>
    <form className="table-toolbar" onSubmit={load}><div className="filter"><label>RFQ ID<input value={rfqId} onChange={(event) => setRfqId(event.target.value)} placeholder="Scan or paste RFQ ID" /></label></div><div className="toolbar-actions"><button className="secondary" disabled={!rfqId.trim()} type="submit">Load quotations</button></div></form>
    {quotes.length > 0 && <div className="table-scroll"><table><thead><tr><th>Select</th><th>Supplier</th><th>Total</th><th>SKU</th><th>Unit cost</th><th>Lead days</th><th>MOQ</th><th>Multiple</th><th>Reliability</th><th>Status</th></tr></thead><tbody>{quotes.map((quote) => quote.lines.map((line, index) => <tr key={`${quote.id}:${line.supplierSku}`}><td>{index === 0 && <input type="radio" name="quotation" checked={selected === quote.id} onChange={() => setSelected(quote.id)} aria-label={`Select quotation ${quote.number}`} />}</td><td>{index === 0 ? quote.supplierOrganizationId : ""}</td><td>{index === 0 ? `${quote.total.currency} ${quote.total.amount.toFixed(2)}` : ""}</td><td>{line.supplierSku}</td><td>{`${line.unitCost.currency} ${line.unitCost.amount.toFixed(2)}`}</td><td>{line.leadTimeDays}</td><td>{line.minimumOrderQuantity}</td><td>{line.orderMultiple}</td><td>{line.reliabilityPercent === null ? "Not supplied" : `${line.reliabilityPercent}%`}</td><td>{index === 0 ? quote.status : ""}</td></tr>))}</tbody></table></div>}
    <div className="table-toolbar"><div className="filter"><label>Award reason<input value={reason} maxLength={500} onChange={(event) => setReason(event.target.value)} placeholder="Document the cost, lead-time, and risk trade-off" /></label></div><div className="toolbar-actions"><button className="primary" type="button" disabled={!selected} onClick={award}>Award selected quotation</button></div></div>
    <div className="notice" aria-live="polite">{notice}</div>
  </div>;
}
