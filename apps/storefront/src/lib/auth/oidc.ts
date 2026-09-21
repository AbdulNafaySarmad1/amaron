import "server-only";
import * as oidc from "openid-client";
import { requiredEnv } from "@/lib/auth/config";

let configuration: Promise<oidc.Configuration> | undefined;

export function oidcConfiguration() {
  const issuer = requiredEnv("OIDC_ISSUER").replace(/\/$/, "");
  const metadataUrl = process.env.OIDC_METADATA_URL;
  const issuerUrl = new URL(issuer);
  const allowInsecure = process.env.OIDC_ALLOW_INSECURE === "true";
  if (allowInsecure && !["localhost", "127.0.0.1"].includes(issuerUrl.hostname)) throw new Error("OIDC_ALLOW_INSECURE is limited to loopback development issuers");
  configuration ??= oidc.discovery(issuerUrl, requiredEnv("OIDC_CLIENT_ID"), process.env.OIDC_CLIENT_SECRET, undefined, {
    [oidc.customFetch]: async (url, options) => {
      const discoveryUrl = `${issuer}/.well-known/openid-configuration`;
      return fetch(metadataUrl && url === discoveryUrl ? metadataUrl : url, options as RequestInit);
    },
    execute: allowInsecure ? [oidc.allowInsecureRequests] : [],
  });
  return configuration;
}
