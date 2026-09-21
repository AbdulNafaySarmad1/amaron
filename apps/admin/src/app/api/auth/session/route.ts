import { NextResponse } from "next/server";
import { currentSession } from "@/lib/auth/session";

export const dynamic = "force-dynamic";
export async function GET() {
  const current = await currentSession();
  return NextResponse.json(current ? { authenticated: true, csrfToken: current.session.csrfToken, user: { name: current.session.user.name } } : { authenticated: false }, { headers: { "Cache-Control": "no-store" } });
}
