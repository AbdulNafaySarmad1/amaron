import { OperationsPage } from "@/components/operations-page";
export default function Page() { return <OperationsPage config={{ apiBase: "/api/admin/supply-chain", title: "Suppliers", eyebrow: "PROCUREMENT / RELATIONSHIPS", description: "Review active legal entities and lifecycle state.", endpoint: "/suppliers", empty: "No suppliers are configured." }} />; }
