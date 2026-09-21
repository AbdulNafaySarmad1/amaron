import { redirect } from "next/navigation";
import { AppShell } from "@/components/app-shell";
import { currentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";
export default async function AdminLayout({ children }: { children: React.ReactNode }) {
  const current = await currentSession();
  if (!current) redirect("/auth/login?returnTo=/admin");
  return <AppShell userName={current.session.user.name ?? current.session.user.email ?? "Operator"} csrfToken={current.session.csrfToken}>{children}</AppShell>;
}
