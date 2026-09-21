import "server-only";
import { createClient, type RedisClientType } from "redis";
import { requiredEnv } from "@/lib/auth/config";

const globalForRedis = globalThis as typeof globalThis & { storefrontRedis?: RedisClientType };

export async function redis() {
  const client = globalForRedis.storefrontRedis ?? createClient({ url: requiredEnv("REDIS_URL") });
  if (!globalForRedis.storefrontRedis) {
    client.on("error", (error) => console.error("Storefront Redis error", error.name));
    globalForRedis.storefrontRedis = client;
  }
  if (!client.isOpen) await client.connect();
  return client;
}
