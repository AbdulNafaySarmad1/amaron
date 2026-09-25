import { NextRequest, NextResponse } from "next/server";
import { requiredEnv } from "@/lib/auth/config";
import { isLocale } from "@/i18n/config";
import { parseProductIds } from "@/lib/product-ids";

export const runtime = "nodejs";

// Public and read-only: product IDs in, at most one related product out. No customer data is involved.
export async function GET(request: NextRequest) {
  const productIds = parseProductIds(request.nextUrl.searchParams.get("ids"));
  if (!productIds) return NextResponse.json({ title: "Bad request", detail: "Provide between 1 and 50 unique product IDs.", status: 400 }, { status: 400 });
  const locale = request.nextUrl.searchParams.get("locale");
  let upstream: Response;
  try {
    upstream = await fetch(`${requiredEnv("API_URL")}/api/catalog/products/addition?productIds=${productIds.join(",")}${isLocale(locale) ? `&locale=${locale}` : ""}`, { headers: { Accept: "application/json" }, signal: AbortSignal.timeout(6_000) });
  } catch {
    return new NextResponse(null, { status: 204 });
  }
  if (upstream.status === 204) return new NextResponse(null, { status: 204 });
  return new NextResponse(upstream.body, { status: upstream.status, headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json", "Cache-Control": "no-store" } });
}
