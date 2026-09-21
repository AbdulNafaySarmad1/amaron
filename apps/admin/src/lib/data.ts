export type Row = Record<string, unknown>;

export function extractRows(payload: unknown): Row[] {
  if (Array.isArray(payload)) return payload.filter(isRow);
  if (!isRow(payload)) return [];
  for (const key of ["items", "results", "data", "records", "recommendations", "alerts"]) {
    const value = payload[key];
    if (Array.isArray(value)) return value.filter(isRow);
  }
  return [];
}

export function isRow(value: unknown): value is Row {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function displayValue(value: unknown) {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (typeof value === "number") return Number.isFinite(value) ? new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 }).format(value) : "—";
  if (typeof value === "string") return value;
  return JSON.stringify(value);
}

export function stableSort(rows: Row[], key: string, direction: "asc" | "desc") {
  return rows.map((row, index) => ({ row, index })).sort((a, b) => {
    const left = a.row[key];
    const right = b.row[key];
    const result = typeof left === "number" && typeof right === "number"
      ? left - right
      : String(left ?? "").localeCompare(String(right ?? ""), undefined, { numeric: true, sensitivity: "base" });
    return (direction === "asc" ? result : -result) || a.index - b.index;
  }).map(({ row }) => row);
}

export function percentChange(current: number, previous: number) {
  if (!Number.isFinite(current) || !Number.isFinite(previous) || previous === 0) return null;
  return ((current - previous) / Math.abs(previous)) * 100;
}

export function chartPoints(values: number[], width = 600, height = 180, padding = 12) {
  const finite = values.filter(Number.isFinite);
  if (!finite.length) return "";
  const min = Math.min(...finite);
  const max = Math.max(...finite);
  const span = max - min;
  return values.map((value, index) => {
    const x = values.length === 1 ? width / 2 : padding + (index / (values.length - 1)) * (width - padding * 2);
    const y = span === 0 ? height / 2 : padding + ((max - value) / span) * (height - padding * 2);
    return `${x.toFixed(2)},${y.toFixed(2)}`;
  }).join(" ");
}

export function csv(rows: Row[], columns: string[]) {
  const escape = (value: unknown) => {
    const text = displayValue(value);
    const safe = /^[=+\-@]/.test(text) ? `'${text}` : text;
    return `"${safe.replaceAll('"', '""')}"`;
  };
  return [columns.map(escape).join(","), ...rows.map((row) => columns.map((column) => escape(row[column])).join(","))].join("\r\n");
}
