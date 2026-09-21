import "server-only";
import { randomBytes } from "node:crypto";
import { cookies } from "next/headers";
import { refreshTokenGrant } from "openid-client";
import { sessionCookieName, sessionTtlSeconds } from "./config";
import { oidcConfiguration } from "./oidc";
import { redis } from "./redis";
import { accessTokenNeedsRefresh } from "./security";

export type StoredSession = { accessToken: string; refreshToken?: string; idToken?: string; accessTokenExpiresAt: number; csrfToken: string; user: { sub: string; name?: string; email?: string } };
const sessionKey = (id: string) => `admin:session:${id}`;
const transactionKey = (state: string) => `admin:oidc:${state}`;
export const randomOpaqueValue = () => randomBytes(32).toString("base64url");

export async function saveLoginTransaction(state: string, value: { codeVerifier: string; nonce: string; returnTo: string }) {
  await (await redis()).setEx(transactionKey(state), 600, JSON.stringify(value));
}
export async function takeLoginTransaction(state: string) {
  const value = await (await redis()).getDel(transactionKey(state));
  return value ? JSON.parse(value) as { codeVerifier: string; nonce: string; returnTo: string } : null;
}
export async function createSession(session: StoredSession) {
  const id = randomOpaqueValue();
  await (await redis()).setEx(sessionKey(id), sessionTtlSeconds, JSON.stringify(session));
  return id;
}
export async function deleteSession(id: string) { await (await redis()).del(sessionKey(id)); }
export async function currentSession() {
  const id = (await cookies()).get(sessionCookieName)?.value;
  if (!id) return null;
  const value = await (await redis()).get(sessionKey(id));
  return value ? { id, session: JSON.parse(value) as StoredSession } : null;
}
export async function validAccessToken(id: string, session: StoredSession) {
  if (!accessTokenNeedsRefresh(session.accessTokenExpiresAt)) return session.accessToken;
  if (!session.refreshToken) { await deleteSession(id); return null; }
  try {
    const tokens = await refreshTokenGrant(await oidcConfiguration(), session.refreshToken);
    const updated: StoredSession = { ...session, accessToken: tokens.access_token, refreshToken: tokens.refresh_token ?? session.refreshToken, idToken: tokens.id_token ?? session.idToken, accessTokenExpiresAt: Date.now() + (tokens.expires_in ?? 60) * 1000 };
    await (await redis()).setEx(sessionKey(id), sessionTtlSeconds, JSON.stringify(updated));
    return updated.accessToken;
  } catch {
    await deleteSession(id);
    return null;
  }
}
