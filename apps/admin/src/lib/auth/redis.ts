import "server-only";
import { createClient, type RedisClientType } from "redis";
import { requiredEnv } from "./config";

const globalRedis = globalThis as typeof globalThis & { adminRedis?: RedisClientType };
export async function redis() {
  const client = globalRedis.adminRedis ?? createClient({ url: requiredEnv("REDIS_URL") });
  if (!globalRedis.adminRedis) {
    client.on("error", (error) => console.error("Admin Redis error", error.name));
    globalRedis.adminRedis = client;
  }
  if (!client.isOpen) await client.connect();
  return client;
}
