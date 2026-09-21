import { NextRequest, NextResponse } from "next/server";
import { currentSession } from "@/lib/auth/session";
import { requiredEnv } from "@/lib/auth/config";

export const runtime = "nodejs";

export async function GET(request: NextRequest) {
  if (!await currentSession()) return NextResponse.redirect(new URL("/api/auth/login?returnTo=/", request.nextUrl.origin));
  const issuer = requiredEnv("OIDC_ISSUER").replace(/\/$/, "");
  return NextResponse.redirect(`${issuer}/account/`);
}
