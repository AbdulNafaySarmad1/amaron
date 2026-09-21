import { OperationsPage } from "@/components/operations-page";
import { CycleCountWorkbench } from "@/components/cycle-count-workbench";
export default function Page() { return <><CycleCountWorkbench /><OperationsPage config={{ apiBase: "/api/admin/supply-chain", title: "Cycle counts", eyebrow: "WAREHOUSE / CONTROL", description: "Run blind physical counts and reconcile discrepancies only after independent review.", endpoint: "/cycle-counts", empty: "No cycle counts are planned." }} /></>; }
