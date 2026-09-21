import { chartPoints } from "@/lib/data";

export function LineChart({ values, label }: { values: number[]; label: string }) {
  const points = chartPoints(values);
  if (!points) return <div className="chart-empty">No series data is available.</div>;
  const summary = `${label}: ${values.length} points, from ${values[0]} to ${values.at(-1)}`;
  return (
    <figure className="chart" aria-label={summary}>
      <svg viewBox="0 0 600 180" role="img" aria-labelledby="chart-title" preserveAspectRatio="none">
        <title id="chart-title">{summary}</title>
        <line x1="12" y1="168" x2="588" y2="168" className="chart-grid" />
        <line x1="12" y1="90" x2="588" y2="90" className="chart-grid" />
        <line x1="12" y1="12" x2="588" y2="12" className="chart-grid" />
        <polyline points={points} className="chart-line" />
      </svg>
      <figcaption className="sr-only">{summary}</figcaption>
    </figure>
  );
}
