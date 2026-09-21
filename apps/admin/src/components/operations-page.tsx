"use client";
import { useEffect, useEffectEvent, useId, useState } from "react";
import { useRouter } from "next/navigation";
import { csv, displayValue, extractRows, isRow, stableSort, type Row } from "@/lib/data";
import { LineChart } from "./line-chart";
import { useSession } from "./session-context";

export type PageConfig = {
  title: string; eyebrow: string; description: string; endpoint: string; empty: string;
  apiBase?: "/api/admin/operations" | "/api/admin/supply-chain";
  chart?: { label: string; fields: string[] };
  approve?: { path: string; label: string };
  action?: { path: string; label: string; selectedField?: string; body?: Row };
  bulkPrices?: boolean;
};

function rowId(row: Row, index: number) {
  for (const key of ["id", "variantId", "sku", "code"]) if (typeof row[key] === "string" || typeof row[key] === "number") return String(row[key]);
  return String(index);
}

export function OperationsPage({ config }: { config: PageConfig }) {
  const router = useRouter();
  const { csrfToken } = useSession();
  const filterId = useId();
  const [rows, setRows] = useState<Row[]>([]);
  const [meta, setMeta] = useState<Row>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [filter, setFilter] = useState("");
  const [sort, setSort] = useState("");
  const [direction, setDirection] = useState<"asc" | "desc">("asc");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [preview, setPreview] = useState<Row | null>(null);
  const [bulkPercent, setBulkPercent] = useState("5");
  const [bulkReason, setBulkReason] = useState("Reviewed bulk price adjustment");

  async function load(signal?: AbortSignal) {
    setLoading(true); setError("");
    try {
      const params = new URLSearchParams({ query: filter, page: String(page), pageSize: String(pageSize) });
      const response = await fetch(`${config.apiBase ?? "/api/admin/operations"}${config.endpoint}?${params}`, { cache: "no-store", signal });
      if (response.status === 401) { router.push(`/auth/login?returnTo=${encodeURIComponent(location.pathname)}`); return; }
      if (!response.ok) throw new Error(response.status === 503 ? "The operations service is unavailable." : `The request failed (${response.status}).`);
      const payload: unknown = await response.json();
      setRows(extractRows(payload)); setMeta(isRow(payload) ? payload : {});
    } catch (reason) {
      if (!(reason instanceof DOMException && reason.name === "AbortError")) setError(reason instanceof Error ? reason.message : "The request could not be completed.");
    } finally { setLoading(false); }
  }
  const loadForEffect = useEffectEvent(load);
  useEffect(() => { const controller = new AbortController(); const timeout = window.setTimeout(() => void loadForEffect(controller.signal), 0); return () => { window.clearTimeout(timeout); controller.abort(); }; }, [config.endpoint, filter, page, pageSize]);

  const columns = Array.from(new Set(rows.flatMap((row) => Object.keys(row)))).filter((key) => !Array.isArray(rows[0]?.[key]) && !isRow(rows[0]?.[key])).slice(0, 8);
  const visibleRows = sort ? stableSort(rows, sort, direction) : rows;
  const selectedRows = rows.filter((row, index) => selected.has(rowId(row, index)));
  const total = typeof meta.total === "number" ? meta.total : rows.length;
  const numericSeries = config.chart ? rows.map((row) => config.chart!.fields.map((field) => row[field]).find((value) => typeof value === "number")).filter((value): value is number => typeof value === "number" && Number.isFinite(value)) : [];

  function changeSort(column: string) { if (sort === column) setDirection(direction === "asc" ? "desc" : "asc"); else { setSort(column); setDirection("asc"); } }
  function saveView() { localStorage.setItem(`admin-view:${config.endpoint}`, JSON.stringify({ filter, sort, direction, pageSize })); setNotice("View saved on this device."); }
  function restoreView() { try { const value = JSON.parse(localStorage.getItem(`admin-view:${config.endpoint}`) ?? "null") as { filter?: string; sort?: string; direction?: "asc" | "desc"; pageSize?: number } | null; if (value) { setFilter(value.filter ?? ""); setSort(value.sort ?? ""); setDirection(value.direction ?? "asc"); setPageSize(value.pageSize ?? 25); setNotice("Saved view restored."); } else setNotice("No saved view is available."); } catch { setNotice("The saved view could not be read."); } }
  function exportCsv() { const blob = new Blob([csv(visibleRows, columns)], { type: "text/csv;charset=utf-8" }); const link = document.createElement("a"); link.href = URL.createObjectURL(blob); link.download = `${config.title.toLowerCase().replaceAll(" ", "-")}.csv`; link.click(); URL.revokeObjectURL(link.href); }
  async function mutate(path: string, body: Row = {}) {
    setNotice("");
    const response = await fetch(`${config.apiBase ?? "/api/admin/operations"}${path}`, { method: "POST", headers: { "Content-Type": "application/json", "x-csrf-token": csrfToken, "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify(body) });
    if (!response.ok) { setNotice(`Action failed (${response.status}). No changes were applied by this client.`); return null; }
    setNotice("Action completed."); await load();
    const payload: unknown = await response.json().catch(() => null);
    return isRow(payload) ? payload : {};
  }
  async function approveSelected() { if (!config.approve) return; for (const id of selected) await mutate(config.approve.path.replace(":id", encodeURIComponent(id))); setSelected(new Set()); }
  async function runAction() {
    if (!config.action) return;
    const selectedRow = selectedRows[0];
    const selectedValue = config.action.selectedField ? selectedRow?.[config.action.selectedField] : undefined;
    await mutate(config.action.path, { ...(config.action.body ?? {}), ...(config.action.selectedField ? { [config.action.selectedField]: selectedValue } : {}) });
  }
  async function previewBulk() {
    const adjustment = Number(bulkPercent);
    if (!Number.isFinite(adjustment) || adjustment <= -100 || adjustment > 100 || bulkReason.trim().length < 3) { setNotice("Enter a percentage between -99.99 and 100 and a reason."); return; }
    const items = selectedRows.flatMap((row) => {
      const price = row.price;
      const current = isRow(price) && typeof price.amount === "number" ? price.amount : typeof row.currentPrice === "number" ? row.currentPrice : null;
      return typeof row.variantId === "string" && current !== null ? [{ variantId: row.variantId, price: Number((current * (1 + adjustment / 100)).toFixed(4)), effectiveFrom: new Date().toISOString(), effectiveUntil: null, reason: bulkReason.trim() }] : [];
    });
    if (items.length !== selectedRows.length) { setNotice("Every selected row must expose a variant ID and current price."); return; }
    const request = { items };
    const result = await mutate("/bulk/prices/preview", request); if (result) setPreview({ ...result, request });
  }
  async function applyBulk() { if (!preview || !isRow(preview.request)) return; await mutate("/bulk/prices/apply", preview.request); setPreview(null); setSelected(new Set()); }

  return <section className="page">
    <header className="page-header"><div><p className="eyebrow">{config.eyebrow}</p><h1>{config.title}</h1><p>{config.description}</p></div><div className="header-actions">
      <button className="secondary" onClick={restoreView}>Load view</button><button className="secondary" onClick={saveView}>Save view</button>
      {config.action && <button className="primary" disabled={Boolean(config.action.selectedField) && selected.size !== 1} onClick={runAction}>{config.action.label}</button>}
    </div></header>
    {config.chart && <div className="panel chart-panel"><div className="panel-heading"><div><h2>{config.chart.label}</h2><p>Series returned by the operations service</p></div><span className="metric">{numericSeries.length} points</span></div><LineChart values={numericSeries} label={config.chart.label} /></div>}
    <div className="panel table-panel">
      <div className="table-toolbar"><div className="filter"><label htmlFor={filterId}>Filter</label><input id={filterId} type="search" value={filter} onChange={(event) => { setFilter(event.target.value); setPage(1); }} placeholder="Search records" /></div><div className="toolbar-actions">
        {config.approve && <button className="primary" disabled={!selected.size} onClick={approveSelected}>{config.approve.label} ({selected.size})</button>}
        {config.bulkPrices && <><label>Adjustment % <input type="number" min="-99.99" max="100" step="0.01" value={bulkPercent} onChange={(event) => setBulkPercent(event.target.value)} /></label><label>Reason <input value={bulkReason} maxLength={500} onChange={(event) => setBulkReason(event.target.value)} /></label><button className="primary" disabled={!selected.size} onClick={previewBulk}>Preview bulk change</button></>}
        <button className="secondary" disabled={!rows.length} onClick={exportCsv}>Export CSV</button><button className="secondary" onClick={() => load()}>Refresh</button>
      </div></div>
      <div aria-live="polite" className="notice">{notice}</div>
      {loading ? <LoadingTable /> : error ? <div className="state error-state"><strong>Unable to load {config.title.toLowerCase()}</strong><p>{error}</p><button className="primary" onClick={() => load()}>Try again</button></div> : !rows.length ? <div className="state"><strong>No records found</strong><p>{config.empty}</p></div> : <div className="table-scroll"><table><thead><tr><th className="select-cell"><input type="checkbox" aria-label="Select all rows" checked={selected.size === rows.length && rows.length > 0} onChange={(event) => setSelected(event.target.checked ? new Set(rows.map(rowId)) : new Set())} /></th>{columns.map((column) => <th key={column} aria-sort={sort === column ? (direction === "asc" ? "ascending" : "descending") : "none"}><button onClick={() => changeSort(column)}>{column.replaceAll(/([A-Z_])/g, " $1")} {sort === column ? (direction === "asc" ? "↑" : "↓") : ""}</button></th>)}</tr></thead><tbody>{visibleRows.map((row, index) => { const id = rowId(row, index); return <tr key={id}><td className="select-cell"><input type="checkbox" aria-label={`Select ${id}`} checked={selected.has(id)} onChange={(event) => { const next = new Set(selected); if (event.target.checked) next.add(id); else next.delete(id); setSelected(next); }} /></td>{columns.map((column) => <td key={column} title={displayValue(row[column])}>{displayValue(row[column])}</td>)}</tr>; })}</tbody></table></div>}
      <footer className="pagination"><span>{total} records</span><label>Rows <select value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}><option>10</option><option>25</option><option>50</option></select></label><button disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</button><span>Page {page}</span><button disabled={rows.length < pageSize} onClick={() => setPage(page + 1)}>Next</button></footer>
    </div>
    {preview && <div className="dialog-backdrop"><div className="preview-dialog" role="dialog" aria-modal="true" aria-label="Bulk price preview"><h2>Review bulk price change</h2><p>The service returned this preview. Apply only after verification.</p><pre>{JSON.stringify(preview, null, 2)}</pre><div><button className="secondary" onClick={() => setPreview(null)}>Cancel</button><button className="danger" onClick={applyBulk}>Apply changes</button></div></div></div>}
  </section>;
}

function LoadingTable() { return <div className="loading-table" aria-label="Loading records" aria-busy="true">{Array.from({ length: 7 }, (_, index) => <span key={index} />)}</div>; }
