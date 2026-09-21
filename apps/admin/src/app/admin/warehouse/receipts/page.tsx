import { OperationsPage } from "@/components/operations-page";
import { ReceivingWorkbench } from "@/components/receiving-workbench";
export default function Page() { return <><ReceivingWorkbench /><OperationsPage config={{ apiBase: "/api/admin/supply-chain", title: "Goods receipts", eyebrow: "WAREHOUSE / PHYSICAL", description: "Inspect posted counts, discrepancies, and accepted, damaged, or quarantined dispositions.", endpoint: "/receipts", empty: "No physical receipts have been posted." }} /></>; }
