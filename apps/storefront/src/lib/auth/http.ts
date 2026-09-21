import { NextResponse } from "next/server";

export function authError(message: string, status = 400) {
  return NextResponse.json({ title: "Authentication error", detail: message, status }, { status, headers: { "Cache-Control": "no-store" } });
}

export function noStore(response: NextResponse) {
  response.headers.set("Cache-Control", "no-store");
  return response;
}
