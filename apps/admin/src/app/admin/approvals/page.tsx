import { OperationsPage } from "@/components/operations-page";
export default function Page() { return <OperationsPage config={{ title: "Approvals", eyebrow: "GOVERNANCE / QUEUE", description: "Review the consolidated queue of operational decisions.", endpoint: "/approvals", empty: "There are no items awaiting approval." }} />; }
