import { timingSafeEqual } from "node:crypto";

export function safeReturnPath(value: string | null | undefined, fallback = "/admin") {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.includes("\\") || /[\u0000-\u001f\u007f]/.test(value)) return fallback;
  try {
    const parsed = new URL(value, "https://admin.invalid");
    return parsed.origin === "https://admin.invalid" ? `${parsed.pathname}${parsed.search}${parsed.hash}` : fallback;
  } catch {
    return fallback;
  }
}

export function validCsrfToken(expected: string, received: string | null) {
  if (!received) return false;
  const left = Buffer.from(expected);
  const right = Buffer.from(received);
  return left.length === right.length && timingSafeEqual(left, right);
}

export function validRequestOrigin(received: string | null, expected: string) {
  if (!received) return false;
  try { return new URL(received).origin === new URL(expected).origin; } catch { return false; }
}

export function isStateChangingMethod(method: string) {
  return !["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase());
}

export function accessTokenNeedsRefresh(expiresAt: number, now = Date.now(), skewMs = 30_000) {
  return expiresAt <= now + skewMs;
}

export function sessionCookieSettings(production: boolean) {
  return { httpOnly: true, secure: production, sameSite: "lax" as const, path: "/" };
}

export function accessTokenRoles(token: string) {
  try {
    const payload = JSON.parse(Buffer.from(token.split(".")[1] ?? "", "base64url").toString("utf8")) as { role?: unknown; resource_access?: Record<string, { roles?: unknown }> };
    const mapped = Array.isArray(payload.role) ? payload.role : [];
    const client = payload.resource_access?.["commerce-api"]?.roles;
    return [...mapped, ...(Array.isArray(client) ? client : [])].filter((role): role is string => typeof role === "string");
  } catch {
    return [];
  }
}

export function hasAdministrationAccess(token: string) {
  return accessTokenRoles(token).includes("administration-access");
}

type Rule = { methods: readonly string[]; pattern: RegExp };
const rules: readonly Rule[] = [
  { methods: ["GET"], pattern: /^\/dashboard$/ },
  { methods: ["GET"], pattern: /^\/variants$/ },
  { methods: ["GET"], pattern: /^\/pricing\/[^/]+$/ },
  { methods: ["POST"], pattern: /^\/pricing\/simulate$/ },
  { methods: ["POST"], pattern: /^\/pricing\/schedules$/ },
  { methods: ["GET"], pattern: /^\/pricing\/recommendations$/ },
  { methods: ["POST"], pattern: /^\/pricing\/[^/]+\/approve$/ },
  { methods: ["GET"], pattern: /^\/demand$/ },
  { methods: ["POST"], pattern: /^\/forecasts\/generate$/ },
  { methods: ["GET"], pattern: /^\/inventory$/ },
  { methods: ["POST"], pattern: /^\/inventory\/(adjustments|transfers)$/ },
  { methods: ["GET"], pattern: /^\/replenishment$/ },
  { methods: ["POST"], pattern: /^\/replenishment\/generate$/ },
  { methods: ["POST"], pattern: /^\/replenishment\/[^/]+\/approve$/ },
  { methods: ["GET", "POST"], pattern: /^\/promotions$/ },
  { methods: ["POST"], pattern: /^\/promotions\/[^/]+\/approve$/ },
  { methods: ["GET"], pattern: /^\/approvals$/ },
  { methods: ["GET"], pattern: /^\/audit$/ },
  { methods: ["GET"], pattern: /^\/alerts$/ },
  { methods: ["POST"], pattern: /^\/alerts\/[^/]+\/acknowledge$/ },
  { methods: ["POST"], pattern: /^\/bulk\/prices\/(preview|apply)$/ },
];

export function isAllowedOperation(method: string, path: string) {
  return rules.some((rule) => rule.methods.includes(method.toUpperCase()) && rule.pattern.test(path));
}
