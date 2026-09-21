"use client";
import { useEffect, useState } from "react";
import { displayValue, extractRows, isRow, type Row } from "@/lib/data";

export function Dashboard() {
  const [dashboard, setDashboard] = useState<Row | null>(null);
  const [alerts, setAlerts] = useState<Row[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    const controller = new AbortController();
    Promise.all([fetch("/api/admin/operations/dashboard", { signal: controller.signal, cache: "no-store" }), fetch("/api/admin/operations/alerts", { signal: controller.signal, cache: "no-store" })])
      .then(async ([summaryResponse, alertResponse]) => {
        if (!summaryResponse.ok) throw new Error(`Dashboard request failed (${summaryResponse.status}).`);
        const summary: unknown = await summaryResponse.json();
        setDashboard(isRow(summary) ? summary : {});
        if (alertResponse.ok) setAlerts(extractRows(await alertResponse.json()));
      })
      .catch((reason: unknown) => { if (!(reason instanceof DOMException && reason.name === "AbortError")) setError(reason instanceof Error ? reason.message : "The dashboard could not be loaded."); })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, []);
  const metrics = dashboard ? Object.values(dashboard).flatMap((value) => Array.isArray(value) ? value.filter(isRow) : []).filter((value) => typeof value.label === "string" && (typeof value.value === "number" || value.value === null)).slice(0, 8) : [];
  return <section className="page">
    <header className="page-header"><div><p className="eyebrow">OPERATIONS / CURRENT STATE</p><h1>Control overview</h1><p>Live operational signals and items requiring attention.</p></div><span className="updated">Live data · no client cache</span></header>
    {loading ? <div className="metric-grid">{Array.from({ length: 4 }, (_, index) => <div className="metric-card skeleton" key={index} />)}</div> : error ? <div className="panel state error-state"><strong>Unable to load the overview</strong><p>{error}</p></div> : <>
      {metrics.length ? <div className="metric-grid">{metrics.map((metric, index) => <article className="metric-card" key={`${String(metric.label)}-${index}`}><span>{displayValue(metric.label)}</span><strong>{displayValue(metric.value)}</strong><small>{displayValue(metric.unit)}</small></article>)}</div> : <div className="panel state"><strong>No summary metrics available</strong><p>The service returned no operational metrics.</p></div>}
      <div className="panel"><div className="panel-heading"><div><h2>Active alerts</h2><p>Conditions reported by the operations service</p></div><span className="metric">{alerts.length} open</span></div>{alerts.length ? <div className="alert-list">{alerts.slice(0, 8).map((alert, index) => <article key={String(alert.id ?? index)}><span className="alert-marker" /><div><strong>{displayValue(alert.title ?? alert.name ?? alert.type)}</strong><p>{displayValue(alert.detail ?? alert.message ?? alert.status)}</p></div></article>)}</div> : <div className="state compact"><strong>No active alerts</strong><p>No alert records were returned.</p></div>}</div>
    </>}
  </section>;
}
