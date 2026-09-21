import { timingSafeEqual } from "node:crypto";

export function safeReturnPath(value: string | null | undefined, fallback = "/") {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.includes("\\") || /[\u0000-\u001f\u007f]/.test(value)) return fallback;
  try {
    const parsed = new URL(value, "https://storefront.invalid");
    if (parsed.origin !== "https://storefront.invalid") return fallback;
    return `${parsed.pathname}${parsed.search}${parsed.hash}`;
  } catch {
    return fallback;
  }
}

export function validCsrfToken(expected: string, received: string | null) {
  if (!received) return false;
  const expectedBytes = Buffer.from(expected);
  const receivedBytes = Buffer.from(received);
  return expectedBytes.length === receivedBytes.length && timingSafeEqual(expectedBytes, receivedBytes);
}

export function validRequestOrigin(received: string | null, expected: string) {
  if (!received) return false;
  try { return new URL(received).origin === new URL(expected).origin; }
  catch { return false; }
}

type ProxyRule = { methods: readonly string[]; pattern: RegExp };

const privateProxyRules: readonly ProxyRule[] = [
  { methods: ["GET"], pattern: /^\/cart$/ },
  { methods: ["PUT"], pattern: /^\/cart\/items$/ },
  { methods: ["DELETE"], pattern: /^\/cart\/items\/[^/]+$/ },
  { methods: ["POST"], pattern: /^\/checkout\/confirm$/ },
  { methods: ["GET"], pattern: /^\/payments\/[^/]+$/ },
  { methods: ["POST"], pattern: /^\/payments\/[^/]+\/confirm$/ },
  { methods: ["GET"], pattern: /^\/orders$/ },
  { methods: ["GET"], pattern: /^\/orders\/[^/]+$/ },
];

export function isAllowedPrivateRequest(method: string, path: string) {
  return privateProxyRules.some((rule) => rule.methods.includes(method) && rule.pattern.test(path));
}

export function isStateChangingMethod(method: string) {
  return !["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase());
}

export function accessTokenNeedsRefresh(expiresAt: number, now = Date.now(), skewMs = 30_000) {
  return expiresAt <= now + skewMs;
}

export function sessionCookieSettings(production: boolean) {
  return {
    httpOnly: true,
    secure: production,
    sameSite: "lax" as const,
    path: "/",
  };
}
