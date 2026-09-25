import { NextRequest, NextResponse } from "next/server";
import { requiredEnv } from "@/lib/auth/config";
import { isLocale } from "@/i18n/config";
import { parseProductIds } from "@/lib/product-ids";

export const runtime = "nodejs";

// Public, read-only card data for device-local lists such as Saved. Prices come from the API, never the browser.
export async function GET(request: NextRequest) {
  const productIds = parseProductIds(request.nextUrl.searchParams.get("ids"));
  if (!productIds) return NextResponse.json({ title: "Bad request", detail: "Provide between 1 and 50 unique product IDs.", status: 400 }, { status: 400 });
  const locale = request.nextUrl.searchParams.get("locale");
  let upstream: Response;
  try {
    upstream = await fetch(`${requiredEnv("API_URL")}/api/catalog/products/batch${isLocale(locale) ? `?locale=${locale}` : ""}`, {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify({ productIds }),
      signal: AbortSignal.timeout(8_000),
    });
  } catch {
    return NextResponse.json({ title: "Service unavailable", detail: "Saved items could not be loaded. Try again.", status: 503 }, { status: 503 });
  }
  return new NextResponse(upstream.body, {
    status: upstream.status,
    headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/json", "Cache-Control": "private, max-age=15" },
  });
}
