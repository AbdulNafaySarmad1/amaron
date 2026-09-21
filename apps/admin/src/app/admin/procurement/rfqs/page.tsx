import { OperationsPage } from "@/components/operations-page";
import { RfqWorkbench } from "@/components/rfq-workbench";
export default function Page() { return <><RfqWorkbench /><OperationsPage config={{ apiBase: "/api/admin/supply-chain", title: "Requests for quotation", eyebrow: "PROCUREMENT / COMPETITION", description: "Invite suppliers and compare transparent cost, lead-time, MOQ, and order-multiple trade-offs before a human award decision.", endpoint: "/rfqs", empty: "No RFQs are available.", approve: { path: "/rfqs/:id/open", label: "Open selected drafts" } }} /></>; }
