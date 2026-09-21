import { OperationsPage } from "@/components/operations-page";
export default function Page() { return <OperationsPage config={{ title: "Audit trail", eyebrow: "GOVERNANCE / AUDIT", description: "Inspect immutable operational activity returned by the audit service.", endpoint: "/audit", empty: "No audit events match the current filter." }} />; }
