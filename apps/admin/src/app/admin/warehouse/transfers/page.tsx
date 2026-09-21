import { OperationsPage } from "@/components/operations-page";
import { TransferWorkbench } from "@/components/transfer-workbench";
export default function Page() { return <><TransferWorkbench /><OperationsPage config={{ apiBase: "/api/admin/supply-chain", title: "Stock transfers", eyebrow: "WAREHOUSE / MOVEMENT", description: "Track stock through draft, in-transit, and received states without exposing moving units as sellable.", endpoint: "/stock-transfers", empty: "No stock transfers are active." }} /></>; }
