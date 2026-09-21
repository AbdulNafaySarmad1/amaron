import "server-only";
import { sessionCookieSettings } from "@/lib/auth/security";

const secureCookies = process.env.SESSION_COOKIE_SECURE ? process.env.SESSION_COOKIE_SECURE === "true" : process.env.NODE_ENV === "production";
export const sessionCookieName = secureCookies ? "__Host-amaron-session" : "amaron-session";
export const loginCookieName = secureCookies ? "__Host-amaron-login" : "amaron-login";
const configuredSessionTtl = Number(process.env.SESSION_TTL_SECONDS ?? 28_800);
if (!Number.isInteger(configuredSessionTtl) || configuredSessionTtl < 300 || configuredSessionTtl > 86_400) throw new Error("SESSION_TTL_SECONDS must be between 300 and 86400");
export const sessionTtlSeconds = configuredSessionTtl;

export const cookieOptions = sessionCookieSettings(secureCookies);

export function requiredEnv(name: string) {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required`);
  return value;
}

export function appUrl(requestOrigin: string) {
  const configured = process.env.APP_URL;
  if (configured) return new URL(configured).origin;
  if (process.env.NODE_ENV === "production") throw new Error("APP_URL is required in production");
  return requestOrigin;
}
