import "server-only";
import { sessionCookieSettings } from "./security";

const secure = process.env.SESSION_COOKIE_SECURE ? process.env.SESSION_COOKIE_SECURE === "true" : process.env.NODE_ENV === "production";
export const sessionCookieName = secure ? "__Host-amaron-admin" : "amaron-admin";
export const loginCookieName = secure ? "__Host-amaron-admin-login" : "amaron-admin-login";
const ttl = Number(process.env.SESSION_TTL_SECONDS ?? 28_800);
if (!Number.isInteger(ttl) || ttl < 300 || ttl > 28_800) throw new Error("SESSION_TTL_SECONDS must be between 300 and 28800");
export const sessionTtlSeconds = ttl;
export const cookieOptions = sessionCookieSettings(secure);

export function requiredEnv(name: string) {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required`);
  return value;
}

export function appUrl(requestOrigin: string) {
  if (process.env.APP_URL) return new URL(process.env.APP_URL).origin;
  if (process.env.NODE_ENV === "production") throw new Error("APP_URL is required in production");
  return requestOrigin;
}
