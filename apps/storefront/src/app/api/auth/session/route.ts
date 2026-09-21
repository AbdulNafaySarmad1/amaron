import { NextResponse } from "next/server";
import { noStore } from "@/lib/auth/http";
import { publicSession } from "@/lib/auth/session";

export const runtime = "nodejs";

export async function GET() {
  const session = await publicSession();
  return noStore(NextResponse.json(session ?? { authenticated: false }));
}
